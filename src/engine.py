"""PavPDF 1.0 — motor local. SPDX-License-Identifier: AGPL-3.0-or-later"""
import sys, os, json, io, math, tempfile, shutil, uuid
from contextlib import contextmanager
from pathlib import Path
import fitz
from PIL import Image, ImageEnhance, ImageFilter

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

def mutate(req):
    with open_pdf(req['input']) as d:
        action=req['action']; ids=page_indices(req,d)
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
                    if action in ('edit','redact'):
                        p.add_redact_annot(r,fill=(1,1,1))
                        p.apply_redactions(images=2,graphics=1,text=0)
                    if action in ('edit','text'):
                        text=req.get('text','')
                        if not text.strip(): raise ValueError('Escribe el texto que deseas insertar.')
                        color=tuple(float(v) for v in req.get('color',[0,0,0]))
                        remain=p.insert_textbox(r,text,fontsize=float(req.get('font_size',11)),
                            fontname='helv',color=color,rotate=p.rotation)
                        if remain<0: raise ValueError('El texto no cabe. Amplía el área o reduce el tamaño de letra.')
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
        return {'output':save(d,req['output'])}

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

def run(req):
    return {'inspect':inspect,'mutate':mutate,'merge':merge,'compress':compress,'split':split,'export':export}[req['cmd']](req)

if __name__=='__main__':
    response=Path(sys.argv[2])
    try:
        req=json.loads(Path(sys.argv[1]).read_text(encoding='utf-8-sig'))
        result={'ok':True,'data':run(req)}
    except Exception as e:
        result={'ok':False,'error':str(e)}
    response.write_text(json.dumps(result,ensure_ascii=False),encoding='utf-8')
