"""Font detection integration tests. Requires the built WindowsOcr.exe and Windows OCR language."""
import sys, json
from pathlib import Path
import fitz
import engine, typography

def run(helper):
    results=[]
    with engine.scratch('pavpdf_type_test_') as temp:
        for family,size,bold in [('Arial',12,False),('Times New Roman',14,False),('Arial',16,True),('Calibri',11,False)]:
            d=fitz.open();p=d.new_page()
            p.insert_text((50,150),'Municipalidad Provincial de Anta',fontsize=size,fontname='custom',fontfile=typography.font_path(family,bold,False))
            digital=typography.digital(p,fitz.Rect(40,120,500,170))
            assert digital['mode']=='pdf' and digital['font_family']==family and digital['font_size']==size and digital['bold']==bold
            pix=p.get_pixmap(dpi=240);scan=fitz.open();q=scan.new_page();q.insert_image(q.rect,stream=pix.tobytes('png'))
            result=typography.detect(q,fitz.Rect(40,120,500,170),fitz.Rect(40,120,500,170),engine.scratch,helper)
            assert result['mode']=='scan' and result['font_family']==family and abs(result['font_size']-size)<=1 and result['bold']==bold,result
            # A scan estimate must never be reported as original metadata.
            assert result['confidence'] in ('media','baja') and 'Estimación' in result['note']
            results.append({'expected':family+' '+str(size),'estimated':result['font_family']+' '+str(result['font_size']),'bold':result['bold']})
            d.close();scan.close()
        d=fitz.open();p=d.new_page();p.insert_text((50,150),'Texto con fuente Times',fontname='tibo',fontsize=13.5)
        p.set_rotation(90);area=fitz.Rect(40,120,500,170);visual=area*p.rotation_matrix
        nr=[visual.x0/p.rect.width,visual.y0/p.rect.height,visual.width/p.rect.width,visual.height/p.rect.height]
        assert abs(typography.digital(p,engine.normalized_rect(p,nr))['font_size']-13.5)<.01
        source=Path(temp)/'source.pdf';out=Path(temp)/'edited.pdf';d.save(source);d.close()
        engine.mutate(dict(input=str(source),output=str(out),action='text',pages=[0],rect=[.2,.3,.5,.15],text='Fuente nueva',font_size=14,font_family='Times New Roman',bold=True))
        with fitz.open(out) as check:
            spans=[s for b in check[0].get_text('dict')['blocks'] for l in b.get('lines',[]) for s in l['spans']]
            assert any('TimesNewRoman' in s['font'] and s['size']==14 and s['flags']&16 for s in spans)
        results.append({'rotation':'coordinates correct','insertion':'chosen font, size and bold embedded'})
        empty=fitz.open();p=empty.new_page();assert typography.digital(p,p.rect) is None;empty.close()
    return results

if __name__=='__main__':print(json.dumps(run(Path(sys.argv[1]).resolve()),ensure_ascii=True,indent=2))
