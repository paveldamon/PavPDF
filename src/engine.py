"""PavPDF 1.0 — motor local. SPDX-License-Identifier: AGPL-3.0-or-later"""
import sys, os, json, io, math, tempfile, shutil, uuid, random
from contextlib import contextmanager
from pathlib import Path
import fitz
from PIL import Image, ImageEnhance, ImageFilter, ImageFont
sys.path.insert(0,str(Path(__file__).resolve().parent))
import typography

@contextmanager
def scratch(prefix):
    # Inherit the current user's ACL, including restricted Windows desktop contexts.
    path=Path(tempfile.gettempdir())/(prefix+uuid.uuid4().hex)
    path.mkdir()
    try: yield str(path)
    finally: shutil.rmtree(path,ignore_errors=True)

def open_pdf(path):
    d = fitz.open(path)
    if d.needs_pass:
        d.close()
        raise ValueError('El PDF tiene contraseña. Abre una copia desbloqueada.')
    if not len(d):
        raise ValueError('El documento no contiene páginas.')
    return d

def save(d, path):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + '.partial')
    try:
        d.save(temp, garbage=4, deflate=True, use_objstms=1)
        with fitz.open(temp) as check:
            if len(check) != len(d): raise ValueError('La verificación del PDF falló.')
        os.replace(temp, path)
    finally:
        if temp.exists(): temp.unlink()
    return str(path)

def render_image(p, dpi=120):
    pix = p.get_pixmap(dpi=dpi, colorspace=fitz.csRGB, alpha=False)
    return Image.frombytes('RGB', (pix.width, pix.height), pix.samples)

def page_indices(req, d):
    ids = [int(i) for i in req.get('pages', range(len(d)))]
    if not ids or any(i < 0 or i >= len(d) for i in ids):
        raise ValueError('Selecciona al menos una página válida.')
    return ids

def blank_score(p):
    # Candidates only: the user reviews the thumbnails before deleting.
    if p.get_text().strip() or p.first_annot is not None or p.first_widget is not None:
        return False, 1.0
    im = render_image(p, 60).convert('L')
    w,h = im.size
    core = im.crop((int(w*.04),int(h*.025),int(w*.96),int(h*.975)))
    hist=core.histogram()
    ink=sum(hist[:185]) / (core.width*core.height)
    return ink < .0015, ink

def inspect(req):
    root=Path(req['cache']); root.mkdir(parents=True, exist_ok=True)
    with open_pdf(req['input']) as d:
        pages=[]
        for i,p in enumerate(d):
            preview=root / ('page_%04d.png'%i)
            im=render_image(p, 105); im.save(preview)
            blank,ink=blank_score(p)
            pages.append({'index':i,'preview':str(preview),'blank':blank,'ink':ink,
                          'width':p.rect.width,'height':p.rect.height})
        return {'pages':pages,'bytes':Path(req['input']).stat().st_size,'signed':d.get_sigflags()>0}

def normalized_rect(p, values):
    r=p.rect
    x,y,w,h=[float(v) for v in values]
    if w<=0 or h<=0: raise ValueError('Dibuja un área sobre la página.')
    visual=fitz.Rect(x*r.width,y*r.height,(x+w)*r.width,(y+h)*r.height) & r
    return visual * p.derotation_matrix



def scan_appearance(p,area):
    """Sample visible ink before erasing it; ignore paper and antialiased edges."""
    if not any((fitz.Rect(info['bbox'])&area).get_area()>.5*area.get_area() for info in p.get_image_info()):
        return None
    visual=area*p.rotation_matrix
    pix=p.get_pixmap(dpi=300,clip=visual,colorspace=fitz.csRGB,alpha=False)
    im=Image.frombytes('RGB',(pix.width,pix.height),pix.samples)
    gray=im.convert('L');hist=gray.histogram();total=sum(hist)
    def percentile(hist,fraction):
        target=sum(hist)*fraction;acc=0
        for value,count in enumerate(hist):
            acc+=count
            if acc>=target:return value
        return 255
    paper=percentile(hist,.9)
    candidates=hist[:max(0,paper-45)]
    if sum(candidates)<12:return None
    # Central ink tone excludes the brightest edges; sampling is robust to isolated specks.
    cutoff=min(paper-45,percentile(candidates,.25)+35)
    samples=[rgb for rgb,luma in zip(im.getdata(),gray.getdata()) if luma<=cutoff]
    if len(samples)<8:return None
    color=tuple(sorted(rgb[c] for rgb in samples)[len(samples)//2]/255 for c in range(3))
    return dict(color=color,softness=.55)

def insert_scan_text(p,origin,text,size,font,appearance):
    """Match the scan tone and gently soften glyph edges at 300 dpi; retain searchable text."""
    scale=300/72
    metrics=fitz.Font(fontfile=font['fontfile']) if 'fontfile' in font else fitz.Font(font['fontname'])
    with fitz.open() as temp:
        page=temp.new_page(width=metrics.text_length(text,fontsize=size)+40,height=size*4+40)
        base=fitz.Point(20,size*2+20)
        page.insert_text(base,text,fontsize=size,**font)
        pix=page.get_pixmap(dpi=300,alpha=True)
        rgba=Image.frombytes('RGBA',(pix.width,pix.height),pix.samples)
        alpha=rgba.getchannel('A').filter(ImageFilter.GaussianBlur(appearance['softness']))
        box=alpha.getbbox()
        if not box:raise ValueError('No se pudo dibujar el texto.')
        alpha=alpha.crop(box)
        out=Image.new('RGBA',alpha.size,tuple(round(c*255) for c in appearance['color'])+(0,));out.putalpha(alpha)
        visual_origin=origin*p.rotation_matrix
        dest=fitz.Rect(visual_origin.x+box[0]/scale-base.x,visual_origin.y+box[1]/scale-base.y,
                       visual_origin.x+box[2]/scale-base.x,visual_origin.y+box[3]/scale-base.y)
        buf=io.BytesIO();out.save(buf,format='PNG')
        p.insert_image(dest*p.derotation_matrix,stream=buf.getvalue(),rotate=p.rotation,keep_proportion=False)
    p.insert_text(origin,text,fontsize=size,color=appearance['color'],render_mode=3,rotate=p.rotation,**font)


def paper_patch(p,visual):
    """Reconstruct paper from nearby light pixels, never clone letters or stamps."""
    area=visual*p.derotation_matrix
    if not any((fitz.Rect(info['bbox'])&area).get_area()>.5*area.get_area() for info in p.get_image_info()):return None
    scale=300/72
    region=fitz.Rect(max(0,visual.x0-3),max(0,visual.y0-18),min(p.rect.width,visual.x1+3),min(p.rect.height,visual.y1+18))
    pix=p.get_pixmap(dpi=300,clip=region,colorspace=fitz.csRGB,alpha=False)
    sample=Image.frombytes('RGB',(pix.width,pix.height),pix.samples)
    gray=sample.convert('L');rgb=sample.load();lum=gray.load()
    left=round(visual.x0*scale-pix.x);top=round(visual.y0*scale-pix.y)
    width=max(1,round(visual.width*scale));height=max(1,round(visual.height*scale))
    if width*height>6000000:raise ValueError('Para conservar el fondo, edita un área más pequeña.')
    out=Image.new('RGB',(width,height));dest=out.load();rng=random.Random(7125)
    # Sample bands above and below; local columns preserve paper shading gradients.
    for x0 in range(0,width,12):
        x1=min(width,x0+12)
        xs=range(max(0,left+x0-8),min(sample.width,left+x1+8),2)
        values=[]
        for yy in range(sample.height):
            if top-3<=yy<=top+height+3:continue
            for xx in xs:values.append((lum[xx,yy],rgb[xx,yy]))
        if len(values)<12:
            values=[(lum[xx,yy],rgb[xx,yy]) for yy in range(sample.height) for xx in xs]
        if not values:return None
        values.sort(key=lambda item:item[0]);paper=values[int((len(values)-1)*.75)][0]
        # Remove text and its antialiased fringe from the source texture samples.
        pool=[color for light,color in values if light>=max(130,paper-28)]
        if not pool:return None
        for yy in range(height):
            for xx in range(x0,x1):dest[xx,yy]=pool[rng.randrange(len(pool))]
    buf=io.BytesIO();out.save(buf,format='PNG')
    return buf.getvalue()

def restore_paper(p,visual,patch):
    if patch:p.insert_image(visual*p.derotation_matrix,stream=patch,rotate=p.rotation,keep_proportion=False)

def flowing_edit(p,req,r):
    """Rebuild one visual scan line, moving its untouched suffix without scaling it."""
    text=req.get('text','').strip()
    if not text or '\n' in text or '\r' in text:
        raise ValueError('Para unir el texto, escribe una sola línea. Desmarca Unir texto siguiente para editar varias líneas.')
    appearance=scan_appearance(p,r) if req.get('match_ink',False) else None
    visual=r*p.rotation_matrix;scale=300/72
    clip=fitz.Rect(0,max(0,visual.y0-4),p.rect.width,min(p.rect.height,visual.y1+4))
    pix=p.get_pixmap(dpi=300,clip=clip,colorspace=fitz.csRGB,alpha=False)
    im=Image.frombytes('RGB',(pix.width,pix.height),pix.samples)
    mask=im.convert('L').point(lambda v:255 if v<160 else 0)
    sx=max(0,round(visual.x0*scale-pix.x));ex=min(im.width,round(visual.x1*scale-pix.x))
    # Locate the actual ink line, not the inflated OCR bounding box.
    rows=[y for y in range(im.height) if mask.crop((sx,y,im.width,y+1)).histogram()[255]>max(3,(im.width-sx)*.002)]
    groups=[]
    for y in rows:
        if not groups or y-groups[-1][-1]>2:groups.append([y])
        else:groups[-1].append(y)
    groups=[g for g in groups if len(g)>=4]
    cy=(visual.y0+visual.y1)*scale/2-pix.y
    if not groups:raise ValueError('No se reconoce una línea impresa. Desmarca Unir texto siguiente para usar el reemplazo normal.')
    group=min(groups,key=lambda g:abs((g[0]+g[-1])/2-cy))
    y0=max(0,group[0]-2);y1=min(im.height,group[-1]+3)
    ink=mask.crop((sx,y0,ex,y1)).getbbox()
    if not ink:raise ValueError('Selecciona las palabras originales que deseas reemplazar.')
    # Reject selections spanning multiple lines instead of deleting adjacent text.
    for g in groups:
        if g is not group and visual.y0*scale-pix.y<=g[0] and g[-1]<=visual.y1*scale-pix.y:
            raise ValueError('Selecciona solo una línea para unir el texto siguiente.')
    start=sx+ink[0];old_end=sx+ink[2]
    suffix=mask.crop((ex,y0,im.width,y1)).getbbox()
    font=typography.insertion_font(req);size=float(req.get('font_size',11))
    family=req.get('font_family','Helvetica')
    path=font.get('fontfile') or typography.font_path({'Helvetica':'Arial','Times-Roman':'Times New Roman','Courier':'Courier New'}.get(family,family),req.get('bold',False),req.get('italic',False))
    if not path:raise ValueError('Selecciona una fuente instalada para unir el texto.')
    glyph=ImageFont.truetype(path,1000)
    box=glyph.getbbox(text,anchor='ls');ink_width=(box[2]-box[0])*size/1000
    original_words=[]
    for word in p.get_text('words'):
        wr=fitz.Rect(word[:4])*p.rotation_matrix
        if visual.contains(fitz.Point((wr.x0+wr.x1)/2,(wr.y0+wr.y1)/2)):
            original_words.append((wr.x0,word[4]))
    original=' '.join(t for x,t in sorted(original_words))
    # Account for descenders in the old word when its OCR text is available.
    old_bottom=glyph.getbbox(original,anchor='ls')[3]*size/1000 if original else 0
    baseline=(pix.y+y0+ink[3])/scale-old_bottom
    top=baseline+box[1]*size/1000;bottom=baseline+box[3]*size/1000
    requested_size=size
    if top<(pix.y+y0)/scale-1 or bottom>(pix.y+y1)/scale+1:
        if not req.get('auto_fit',False):
            raise ValueError('Activa Ajustar altura automáticamente o reduce el tamaño de letra.')
        band_top=(pix.y+y0)/scale+.25;band_bottom=(pix.y+y1)/scale-.25
        available=band_bottom-band_top
        fitted=min(size,available*1000/max(1,box[3]-box[1]))
        if fitted<4:raise ValueError('La selección es demasiado pequeña. Incluye la altura completa de las palabras.')
        size=math.floor(fitted*100)/100
        baseline=max(band_top-box[1]*size/1000,min(baseline,band_bottom-box[3]*size/1000))
        ink_width=(box[2]-box[0])*size/1000
    new_end=start+ink_width*scale
    dx=round(new_end-old_end)
    tail=None;tail_rect=None
    if suffix:
        tx=ex+suffix[0];end=ex+suffix[2]
        if tx-old_end>size*scale*2:
            raise ValueError('El texto siguiente parece otra columna. Desmarca Unir texto siguiente para conservar su posición.')
        if end+dx>im.width-2 or tx+dx<new_end-1:
            raise ValueError('El texto nuevo no cabe en la línea. Acórtalo o reemplaza el párrafo completo con Unir texto siguiente desmarcado.')
        # Preserve suffix pixels and spacing exactly; no OCR rewriting or stretching.
        tail=im.crop((tx,y0,end,y1))
        tail_rect=fitz.Rect((pix.x+tx+dx)/scale,(pix.y+y0)/scale,(pix.x+end+dx)/scale,(pix.y+y1)/scale)
        erase_end=max(end,end+dx)+2
    else:erase_end=max(old_end,new_end)+2
    erase=fitz.Rect((pix.x+start-1)/scale,(pix.y+y0)/scale,(pix.x+erase_end)/scale,(pix.y+y1)/scale)
    # The new text and suffix must stay entirely inside this one line's band.
    if erase.x1>p.rect.width:raise ValueError('El texto supera el ancho de la página.')
    if req.get('protect_header',True) and erase.y0<p.rect.height*float(req.get('protect',.16)):
        raise ValueError('La línea toca el membrete protegido. Autoriza el cambio o ajusta la selección.')
    paper=paper_patch(p,erase) if req.get('match_paper',False) else None
    p.add_redact_annot(erase*p.derotation_matrix,fill=(1,1,1))
    p.apply_redactions(images=2,graphics=0,text=0)
    restore_paper(p,erase,paper)
    if tail is not None:
        buf=io.BytesIO();tail.save(buf,format='PNG')
        p.insert_image(tail_rect*p.derotation_matrix,stream=buf.getvalue(),rotate=p.rotation,keep_proportion=False)
    origin=fitz.Point((pix.x+start)/scale-box[0]*size/1000,baseline)*p.derotation_matrix
    if appearance:insert_scan_text(p,origin,text,size,font,appearance)
    else:p.insert_text(origin,text,fontsize=size,color=tuple(req.get('color',[0,0,0])),rotate=p.rotation,**font)
    return size

def mutate(req):
    with open_pdf(req['input']) as d:
        action=req['action']; ids=page_indices(req,d); fitted_sizes=[]
        if action=='delete':
            if len(set(ids)) >= len(d): raise ValueError('Debe quedar al menos una página.')
            d.delete_pages(sorted(set(ids)))
        elif action=='rotate':
            for i in ids: d[i].set_rotation((d[i].rotation+int(req.get('angle',90)))%360)
        elif action=='reorder':
            order=[int(x) for x in req['order']]
            if sorted(order)!=list(range(len(d))): raise ValueError('Orden de páginas inválido.')
            d.select(order)
        elif action=='duplicate':
            order=[]
            for i in range(len(d)):
                order.append(i)
                if i in ids: order.append(i)
            d.select(order)
        elif action in ('edit','redact','text','crop','image'):
            for i in ids:
                p=d[i]; r=normalized_rect(p,req['rect'])
                if action=='crop':
                    # CropBox coordinates are unrotated page coordinates.
                    p.set_cropbox(r)
                elif action=='image':
                    p.insert_image(r,filename=req['image'],keep_proportion=True)
                else:
                    protect=float(req.get('protect',.16))
                    if float(req['rect'][1])<protect and req.get('protect_header',True):
                        raise ValueError('El área invade el membrete protegido. Ajusta la selección o desactiva la protección.')
                    if action=='edit' and req.get('close_gap',False):
                        fitted_sizes.append(flowing_edit(p,req,r))
                        continue
                    appearance=scan_appearance(p,r) if action=='edit' and req.get('match_ink',False) else None
                    paper=paper_patch(p,r*p.rotation_matrix) if action=='edit' and req.get('match_paper',False) else None
                    if action in ('edit','redact'):
                        p.add_redact_annot(r,fill=(1,1,1))
                        p.apply_redactions(images=2,graphics=1,text=0)
                        restore_paper(p,r*p.rotation_matrix,paper)
                    if action in ('edit','text'):
                        text=req.get('text','')
                        if not text.strip(): raise ValueError('Escribe el texto que deseas insertar.')
                        color=appearance['color'] if appearance else tuple(float(v) for v in req.get('color',[0,0,0]))
                        font=typography.insertion_font(req)
                        size=float(req.get('font_size',11))
                        visual=r*p.rotation_matrix
                        metrics=fitz.Font(fontfile=font['fontfile']) if 'fontfile' in font else fitz.Font(font['fontname'])
                        width=metrics.text_length(text,fontsize=size)
                        if '\n' not in text and req.get('auto_fit',False):
                            if 'fontfile' in font:
                                bb=ImageFont.truetype(font['fontfile'],1000).getbbox(text,anchor='ls')
                                ink_height=(bb[3]-bb[1])*size/1000
                            else:
                                boxes=[metrics.glyph_bbox(ord(c)) for c in text if not c.isspace()]
                                ink_height=(max(b.y1 for b in boxes)-min(b.y0 for b in boxes))*size
                            size=math.floor(min(size,size*visual.width/max(width,.01),size*visual.height/max(ink_height,.01))*100)/100
                            if size<4:raise ValueError('El texto necesita un área mayor para conservar una letra legible.')
                            width=metrics.text_length(text,fontsize=size)
                        if '\n' not in text and width<=visual.width:
                            # Textbox layout reserves an entire line even for one word.
                            # Place the actual glyphs inside the selection instead.
                            boxes=[metrics.glyph_bbox(ord(c)) for c in text if not c.isspace()]
                            top=max(b.y1 for b in boxes)*size
                            bottom=min(b.y0 for b in boxes)*size
                            if 'fontfile' in font:
                                glyphs=ImageFont.truetype(font['fontfile'],1000).getbbox(text,anchor='ls')
                                top=-glyphs[1]*size/1000
                                bottom=-glyphs[3]*size/1000
                            if top-bottom>visual.height:
                                raise ValueError('La letra supera la altura seleccionada. Amplía el área o reduce el tamaño.')
                            origin=fitz.Point(visual.x0,visual.y0+(visual.height-(top-bottom))/2+top)*p.derotation_matrix
                            if appearance:insert_scan_text(p,origin,text,size,font,appearance)
                            else:p.insert_text(origin,text,fontsize=size,color=color,rotate=p.rotation,**font)
                        else:
                            remain=p.insert_textbox(r,text,fontsize=size,color=color,rotate=p.rotation,**font)
                            while remain<0 and req.get('auto_fit',False) and size>4:
                                size=max(4,round(size-.25,2))
                                remain=p.insert_textbox(r,text,fontsize=size,color=color,rotate=p.rotation,**font)
                            if remain<0: raise ValueError('El texto no cabe. Amplía el área o reduce el tamaño de letra.')
                        fitted_sizes.append(size)
        elif action=='number':
            for n,i in enumerate(ids):
                p=d[i]; visual=fitz.Rect(25,p.rect.height-25,p.rect.width-25,p.rect.height-8)
                p.insert_textbox(visual*p.derotation_matrix,str(n+int(req.get('start',1))),
                    fontsize=9,fontname='helv',align=2,rotate=p.rotation,color=(.3,.3,.3))
        elif action=='enhance':
            # Keep header pixels exactly as rendered; adjust only the body. No AI reconstruction.
            for i in ids:
                p=d[i]; im=render_image(p,240)
                header=int(im.height*float(req.get('protect',.16))) if req.get('protect_header',True) else 0
                body=im.crop((0,header,im.width,im.height))
                body=ImageEnhance.Contrast(body).enhance(float(req.get('contrast',1.08)))
                body=ImageEnhance.Brightness(body).enhance(1.015)
                im.paste(body,(0,header)); b=io.BytesIO(); im.save(b,format='PNG',optimize=True)
                # Replace only selected page; rendering preserves its visible orientation.
                rect=p.rect; d.delete_page(i); q=d.new_page(pno=i,width=rect.width,height=rect.height)
                q.insert_image(q.rect,stream=b.getvalue())
        else: raise ValueError('Operación no reconocida: '+action)
        return {'output':save(d,req['output']),'font_sizes':fitted_sizes}

def merge(req):
    d=fitz.open()
    try:
        for f in req['files']:
            if Path(f).suffix.lower()=='.pdf':
                with open_pdf(f) as s: d.insert_pdf(s)
            else:
                with Image.open(f) as im:
                    im=im.convert('RGB'); b=io.BytesIO(); im.save(b,format='PNG')
                    p=d.new_page(width=595.276,height=841.89)
                    p.insert_image(p.rect,stream=b.getvalue())
        if not len(d): raise ValueError('Agrega documentos primero.')
        return {'output':save(d,req['output'])}
    finally: d.close()

def compress(req):
    with open_pdf(req['input']) as d:
        if req.get('mode','lossless')=='lossless':
            save(d,req['output'])
            if Path(req['output']).stat().st_size>Path(req['input']).stat().st_size:
                shutil.copyfile(req['input'],req['output'])
        else:
            # Re-encode image streams, leaving PDF text, vectors, geometry and page structure intact.
            seen=set(); dpi=int(req.get('dpi',240)); quality=int(req.get('quality',88))
            for p in d:
                for info in p.get_images(full=True):
                    x=info[0]
                    if x in seen or info[1]: continue # keep alpha masks intact
                    seen.add(x)
                    try:
                        raw=d.extract_image(x)['image']; im=Image.open(io.BytesIO(raw))
                        if im.mode in ('1','P') or min(im.size)<150: continue
                        placements=p.get_image_rects(x)
                        if not placements: continue
                        width=max(abs(r.width) for r in placements);height=max(abs(r.height) for r in placements)
                        scale=min(1,max(width*dpi/72/im.width,height*dpi/72/im.height))
                        if scale<1: im=im.resize((max(1,int(im.width*scale)),max(1,int(im.height*scale))),Image.Resampling.LANCZOS)
                        b=io.BytesIO(); im.convert('RGB').save(b,format='JPEG',quality=quality,optimize=True)
                        if len(b.getvalue())<len(raw): p.replace_image(x,stream=b.getvalue())
                    except (ValueError,OSError): continue
            save(d,req['output'])
            if Path(req['output']).stat().st_size>Path(req['input']).stat().st_size:
                shutil.copyfile(req['input'],req['output'])
    return {'output':req['output'],'bytes':Path(req['output']).stat().st_size}

def split(req):
    limit=int(float(req.get('max_mb',10))*1000000)
    if limit<10000: raise ValueError('El límite es demasiado pequeño.')
    dest=Path(req['folder']); dest.mkdir(parents=True,exist_ok=True)
    # Stage everything so an oversized single page cannot leave an incomplete delivery.
    with scratch('pavpdf_split_') as stage,open_pdf(req['input']) as d:
        outputs=[]; start=0; part=1
        while start<len(d):
            best=None; end=start
            while end<len(d):
                test=fitz.open(); test.insert_pdf(d,from_page=start,to_page=end)
                data=test.tobytes(garbage=4,deflate=True,use_objstms=1);test.close()
                if len(data)>limit: break
                best=data;end+=1
            if best is None:
                raise ValueError('La página %d supera por sí sola el límite. Usa Compresión de escaneos y vuelve a dividir. No se exportó ningún archivo.'%(start+1))
            name='PavPDF_parte_%03d.pdf'%part
            (Path(stage)/name).write_bytes(best)
            outputs.append({'name':name,'from':start+1,'to':end,'bytes':len(best)})
            start=end;part+=1
        # Do not overwrite previous exports.
        if any((dest/o['name']).exists() for o in outputs):
            raise ValueError('La carpeta ya contiene partes con estos nombres. Elige una carpeta vacía.')
        for o in outputs: shutil.copyfile(Path(stage)/o['name'],dest/o['name'])
        return {'outputs':outputs,'folder':str(dest)}

def export(req):
    with open_pdf(req['input']) as d:
        if req.get('kind')=='images':
            root=Path(req['folder']);root.mkdir(parents=True,exist_ok=True)
            names=[root/('pagina_%04d.png'%(i+1)) for i in page_indices(req,d)]
            if any(p.exists() for p in names): raise ValueError('Ya existen imágenes con esos nombres. Elige otra carpeta.')
            for i,name in zip(page_indices(req,d),names): render_image(d[i],200).save(name)
            return {'folder':str(root)}
        if req.get('kind')=='extract': d.select(page_indices(req,d))
        return {'output':save(d,req['output'])}

def detect_font(req):
    with open_pdf(req['input']) as d:
        p=d[int(req['page'])];area=normalized_rect(p,req['rect'])
        visual=area*p.rotation_matrix
        return typography.detect(p,area,visual,scratch,Path(__file__).parent/'WindowsOcr.exe')

def preview(req):
    with open_pdf(req['input']) as d:
        p=d[int(req['page'])];dpi=min(300,max(72,int(req.get('dpi',144))))
        # Limit preview memory for unusually large PDF paper sizes.
        pixels=p.rect.width*p.rect.height*(dpi/72)**2
        if pixels>18000000:dpi=max(12,int(dpi*math.sqrt(18000000/pixels)))
        render_image(p,dpi).save(req['output'])
        return {'output':req['output'],'dpi':dpi}

def run(req):
    return {'inspect':inspect,'mutate':mutate,'merge':merge,'compress':compress,'split':split,'export':export,
            'detect_font':detect_font,'preview':preview}[req['cmd']](req)

if __name__=='__main__':
    response=Path(sys.argv[2])
    try:
        req=json.loads(Path(sys.argv[1]).read_text(encoding='utf-8-sig'))
        result={'ok':True,'data':run(req)}
    except Exception as e:
        result={'ok':False,'error':str(e)}
    response.write_text(json.dumps(result,ensure_ascii=False),encoding='utf-8')
