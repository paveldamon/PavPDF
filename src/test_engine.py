"""Pruebas de preservación, edición, paginación y límites. No necesita red."""
import sys, tempfile, io, json
from pathlib import Path
import fitz
from PIL import Image, ImageChops
import engine

def test_all(real=None):
    checks=[]
    with engine.scratch('pavpdf_tests_') as t:
        root=Path(t)
        source=root/'source.pdf'
        d=fitz.open()
        p=d.new_page();p.insert_text((40,40),'MEMBRETE INSTITUCIONAL',fontsize=16,color=(.1,.3,.7));p.insert_text((40,180),'Texto original',fontsize=12)
        d.new_page()
        p=d.new_page();p.insert_text((40,150),'Ultima pagina',fontsize=12)
        d.save(source);d.close()
        with engine.open_pdf(source) as d:
            assert [engine.blank_score(p)[0] for p in d]==[False,True,False]
        checks.append('Blancos: protege páginas con texto y detecta hoja vacía')
        clean=root/'clean.pdf';engine.mutate(dict(input=str(source),output=str(clean),action='delete',pages=[1]))
        with fitz.open(clean) as d: assert len(d)==2 and 'Ultima' in d[1].get_text()
        checks.append('Eliminación mantiene orden y contenido')
        edit=root/'edit.pdf';engine.mutate(dict(input=str(clean),output=str(edit),action='edit',pages=[0],rect=[.05,.18,.65,.08],text='Texto corregido',font_size=12))
        with fitz.open(edit) as d:
            assert 'corregido' in d[0].get_text() and 'original' not in d[0].get_text()
            assert 'MEMBRETE' in d[0].get_text()
        checks.append('Reemplazo elimina texto anterior y conserva membrete')
        try: engine.mutate(dict(input=str(clean),output=str(root/'forbidden.pdf'),action='redact',pages=[0],rect=[.01,.01,.5,.1]))
        except ValueError: pass
        else: raise AssertionError('Protección de membrete no aplicada')
        assert not (root/'forbidden.pdf').exists()
        checks.append('Protección rechaza cambios en membrete')
        rot=root/'rotate.pdf';engine.mutate(dict(input=str(clean),output=str(rot),action='rotate',pages=[0],angle=90))
        with fitz.open(rot) as d: assert d[0].rotation==90
        rotedit=root/'rotedit.pdf';engine.mutate(dict(input=str(rot),output=str(rotedit),action='text',pages=[0],rect=[.2,.4,.5,.15],text='Rotado correcto',font_size=11))
        with fitz.open(rotedit) as d: assert 'Rotado' in d[0].get_text()
        checks.append('Rotación y texto sobre página rotada')
        optimized=root/'optimized.pdf';engine.compress(dict(input=str(edit),output=str(optimized),mode='lossless'))
        with fitz.open(edit) as a,fitz.open(optimized) as b:
            for i in range(len(a)): assert a[i].get_pixmap().samples==b[i].get_pixmap().samples
        checks.append('Optimización sin pérdida: píxeles idénticos')
        merged=root/'merged.pdf';engine.merge(dict(files=[str(clean),str(clean)],output=str(merged)))
        with fitz.open(merged) as d: assert len(d)==4
        checks.append('Unión conserva todas las páginas')
        noisy=root/'noise.pdf';d=fitz.open()
        for i in range(4):
            im=Image.effect_noise((650,850),80).convert('RGB');b=io.BytesIO();im.save(b,format='JPEG',quality=95)
            p=d.new_page();p.insert_image(p.rect,stream=b.getvalue());p.insert_text((30,30),'Page %d'%i)
        d.save(noisy);d.close()
        result=engine.split(dict(input=str(noisy),folder=str(root/'parts'),max_mb=.9))
        count=0
        for item in result['outputs']:
            f=root/'parts'/item['name'];assert f.stat().st_size<=900000
            with fitz.open(f) as d: count+=len(d)
        assert count==4 and len(result['outputs'])>1
        checks.append('División: límite exacto, todas las páginas una sola vez')
        try: engine.split(dict(input=str(noisy),folder=str(root/'toosmall'),max_mb=.02))
        except ValueError: pass
        else: raise AssertionError('Debe rechazar página que excede límite')
        assert not list((root/'toosmall').glob('*.pdf'))
        checks.append('Página demasiado grande: error sin entrega incompleta')
        enhanced=root/'enhanced.pdf';engine.mutate(dict(input=str(clean),output=str(enhanced),action='enhance',pages=[0],protect_header=True,protect=.16))
        with fitz.open(clean) as a,fitz.open(enhanced) as b:
            original=engine.render_image(a[0],240)
            extracted=Image.open(io.BytesIO(b.extract_image(b[0].get_images()[0][0])['image']))
            h=int(original.height*.16)
            assert ImageChops.difference(original.crop((0,0,original.width,h)),extracted.crop((0,0,extracted.width,h))).getbbox() is None
        checks.append('Mejora: membrete conserva exactamente sus píxeles renderizados')
        crop=root/'crop.pdf';engine.mutate(dict(input=str(clean),output=str(crop),action='crop',pages=[0],rect=[.05,.05,.9,.9]))
        with fitz.open(crop) as d: assert d[0].rect.width<595
        checks.append('Recorte válido')
        try: engine.mutate(dict(input=str(clean),output=str(root/'empty.pdf'),action='delete',pages=[0,1]))
        except ValueError: pass
        else: raise AssertionError('No debe eliminar todas las páginas')
        checks.append('Impide documento vacío')
        if real:
            with engine.open_pdf(real) as d:
                blanks=[i+1 for i,p in enumerate(d) if engine.blank_score(p)[0]]
                assert blanks==[2,6,12,14],blanks
            parts=engine.split(dict(input=real,folder=str(root/'realparts'),max_mb=10))
            assert all(p['bytes']<=10000000 for p in parts['outputs'])
            assert sum(p['to']-p['from']+1 for p in parts['outputs'])==14
            checks.append('Informe real: detecta las cuatro hojas y divide en partes ≤10 MB')
    return checks

if __name__=='__main__':
    result=test_all(sys.argv[1] if len(sys.argv)>1 else None)
    print(json.dumps({'passed':len(result),'checks':result},ensure_ascii=False,indent=2))
