"""Local PDF font inspection and explicitly approximate scan matching. AGPL-3.0-or-later."""
import os, re, io, json, math, subprocess, statistics, hashlib
from pathlib import Path
from collections import Counter
import fitz
from PIL import Image, ImageFont, ImageDraw, ImageOps, ImageFilter, ImageChops, ImageStat

# Use fonts installed by Windows; do not redistribute their files.
FONTS={
 'Arial':('arial.ttf','arialbd.ttf','ariali.ttf','arialbi.ttf'),
 'Arial Narrow':('arialn.ttf','arialnb.ttf','arialni.ttf','arialnbi.ttf'),
 'Times New Roman':('times.ttf','timesbd.ttf','timesi.ttf','timesbi.ttf'),
 'Calibri':('calibri.ttf','calibrib.ttf','calibrii.ttf','calibriz.ttf'),
 'Cambria':('cambria.ttc','cambriab.ttf','cambriai.ttf','cambriaz.ttf'),
 'Segoe UI':('segoeui.ttf','segoeuib.ttf','segoeuii.ttf','segoeuiz.ttf'),
 'Tahoma':('tahoma.ttf','tahomabd.ttf',None,None),
 'Verdana':('verdana.ttf','verdanab.ttf','verdanai.ttf','verdanaz.ttf'),
 'Courier New':('cour.ttf','courbd.ttf','couri.ttf','courbi.ttf'),
}
BASE={'Helvetica':('helv','hebo','heit','hebi'),'Times-Roman':('tiro','tibo','tiit','tibi'),'Courier':('cour','cobo','coit','cobi')}

def font_path(family,bold=False,italic=False):
    names=FONTS.get(family)
    if not names:return None
    name=names[int(bool(bold))+2*int(bool(italic))]
    if not name:return None
    p=Path(os.environ.get('WINDIR',r'C:\Windows'))/'Fonts'/name
    return str(p) if p.exists() else None

def insertion_font(req):
    family=req.get('font_family','Helvetica');bold=req.get('bold',False);italic=req.get('italic',False)
    path=font_path(family,bold,italic)
    if path:return dict(fontname='PavFont'+hashlib.sha256(path.lower().encode()).hexdigest()[:12],fontfile=path)
    if family not in BASE:
        raise ValueError('La fuente o variante elegida no está instalada. Selecciona otra fuente.')
    return dict(fontname=BASE[family][int(bool(bold))+2*int(bool(italic))])

def normalize(name):return re.sub('[^a-z0-9]','',name.lower().split('+')[-1])

def identify(name):
    n=normalize(name)
    if n.startswith('arialnarrow'):return 'Arial Narrow'
    for family in FONTS:
        if n.startswith(normalize(family)):return family
    if 'timesnewroman' in n:return 'Times New Roman'
    if 'helvetica' in n:return 'Helvetica'
    if n.startswith('times'):return 'Times-Roman'
    if n.startswith('courier'):return 'Courier'
    return 'Arial'

def digital(p,area):
    # Invisible OCR describes the search layer, not the visible scanned font.
    hidden=[fitz.Rect(t['bbox']) for t in p.get_texttrace()
            if t['type']==3 or t.get('opacity',1)==0]
    spans=[]
    for block in p.get_text('dict',flags=fitz.TEXTFLAGS_TEXT)['blocks']:
        for line in block.get('lines',[]):
            for s in line['spans']:
                box=fitz.Rect(s['bbox']);inter=box&area
                if s['text'].strip() and inter.width>0 and inter.height>0 and inter.get_area()/max(1,box.get_area())>.2:
                    if s.get('alpha',255)==0:continue
                    if 'glyphless' in s['font'].lower():return None
                    if any((box&h).get_area()>.8*box.get_area() for h in hidden):continue
                    spans.append((s,max(1,len(s['text'].strip()))*inter.get_area()/max(1,box.get_area())))
    if not spans:return None
    groups=Counter()
    for s,w in spans:groups[(s['font'],round(s['size'],2),s['flags'])]+=w
    name,size,flags=groups.most_common(1)[0][0];family=identify(name)
    bold=bool(flags&16) or 'bold' in name.lower();italic=bool(flags&2) or any(x in name.lower() for x in ('italic','oblique'))
    available=family in BASE or font_path(family,bold,italic)
    if not available:family='Helvetica'
    fallback=normalize(name) not in (normalize(family),normalize(family)+'regular') and identify(name)=='Arial' and not normalize(name).startswith('arial')
    return dict(mode='pdf',source_font=name,font_family=family,font_size=size,bold=bold,italic=italic,
        mixed=len(groups)>1,confidence='metadata',text=' '.join(s['text'] for s,w in spans),
        note=('Fuente original no disponible; se propone '+family+'. ') if fallback or not available else '')

def ink_crop(im):
    gray=im.convert('L');mask=gray.point(lambda x:255 if x<180 else 0)
    box=mask.getbbox()
    if not box:return None
    return ImageOps.invert(gray).crop(box)

def template(text,path):
    font=ImageFont.truetype(path,100);bbox=font.getbbox(text)
    im=Image.new('L',(max(1,bbox[2]-bbox[0])+10,max(1,bbox[3]-bbox[1])+10),255)
    ImageDraw.Draw(im).text((5-bbox[0],5-bbox[1]),text,font=font,fill=0)
    return ink_crop(im)

def feature(im):
    return im.resize((160,40),Image.Resampling.LANCZOS).filter(ImageFilter.GaussianBlur(.7))

def estimate(im,words,dpi):
    samples=[]
    # Compare several words of one dominant line height to avoid mixing headings and body text.
    for w in words:
        text=w['text'].strip()
        if len(text)<4 or not any(c.isalpha() for c in text):continue
        x,y,ww,hh=[float(w[k]) for k in ('x','y','width','height')]
        crop=im.crop((max(0,int(x)-2),max(0,int(y)-2),min(im.width,math.ceil(x+ww)+2),min(im.height,math.ceil(y+hh)+2)))
        ink=ink_crop(crop)
        if ink is not None and ink.height>=6 and ink.width>10:samples.append((text,ink))
    if not samples:raise ValueError('No hay suficiente texto impreso para estimar la letra. Selecciona varias palabras de una misma línea.')
    height=statistics.median(x[1].height for x in samples)
    samples=[x for x in samples if .7*height<=x[1].height<=1.4*height]
    samples=sorted(samples,key=lambda x:len(x[0]),reverse=True)[:6]
    scores=[]
    for family in FONTS:
        for bold,italic in ((False,False),(True,False),(False,True),(True,True)):
            path=font_path(family,bold,italic)
            if not path:continue
            errors=[];sizes=[]
            for text,ink in samples:
                ref=template(text,path)
                if ref is None:continue
                error=ImageStat.Stat(ImageChops.difference(feature(ink),feature(ref))).mean[0]/255
                aspect=abs(math.log((ink.width/ink.height)/(ref.width/ref.height)))
                errors.append(error+.22*min(aspect,2))
                scale=math.sqrt((ink.width/ref.width)*(ink.height/ref.height))
                sizes.append(100*scale*72/dpi)
            if errors:scores.append((statistics.mean(errors),family,bold,italic,statistics.median(sizes)))
    if not scores:raise ValueError('No se encontraron fuentes instaladas para comparar el escaneo.')
    scores.sort();best=scores[0];gap=scores[1][0]-best[0] if len(scores)>1 else 0
    if best[0]>.36:raise ValueError('La imagen no permite reconocer una fuente fiable. Prueba con texto impreso más nítido y horizontal.')
    return dict(mode='scan',source_font=best[1],font_family=best[1],font_size=round(min(300,max(1,best[4]))*2)/2,
        bold=best[2],italic=best[3],confidence='media' if gap>.012 and best[0]<.2 else 'baja',mixed=False,
        note='Estimación visual entre fuentes habituales; no identifica con certeza la fuente original.',
        score=round(best[0],4),samples=len(samples),alternatives=[x[1] for x in scores[:3]])

def detect(p,area,visual,scratch,helper):
    result=digital(p,area)
    if result:return result
    dpi=300
    # Render only the visible selected region; maximum dimension stays within Windows OCR limits.
    dpi=min(dpi,math.floor(2400*72/max(visual.width,visual.height)))
    dpi=max(36,dpi)
    pix=p.get_pixmap(matrix=fitz.Matrix(dpi/72,dpi/72),clip=visual,colorspace=fitz.csRGB,alpha=False)
    im=Image.frombytes('RGB',(pix.width,pix.height),pix.samples)
    if im.width<12 or im.height<12:raise ValueError('Selecciona un área con varias palabras.')
    if not Path(helper).exists():raise ValueError('No se encontró el componente de reconocimiento local. Reinstala PavPDF.')
    with scratch('pavpdf_font_') as temp:
        inp=Path(temp)/'region.png';out=Path(temp)/'ocr.json';im.save(inp)
        flags=getattr(subprocess,'CREATE_NO_WINDOW',0)
        try:subprocess.run([str(helper),str(inp),str(out)],timeout=40,creationflags=flags,capture_output=True,check=True)
        except subprocess.TimeoutExpired:raise ValueError('El reconocimiento tardó demasiado. Selecciona menos texto.')
        except subprocess.CalledProcessError:raise ValueError('Windows no pudo reconocer el texto. Puedes elegir la fuente manualmente.')
        if not out.exists():raise ValueError('Windows no devolvió un resultado de reconocimiento.')
        ocr=json.loads(out.read_text(encoding='utf-8-sig'))
        if not ocr.get('ok'):raise ValueError(ocr.get('error','Reconocimiento no disponible.'))
        if abs(float(ocr.get('angle') or 0))>5:raise ValueError('Gira el documento para que el texto esté horizontal y vuelve a seleccionar.')
        result=estimate(im,ocr.get('words',[]),dpi);result['text']=ocr.get('text','');return result
