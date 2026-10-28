import sys,tempfile
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import fitz,engine,typography

def run():
 with engine.scratch('edit_regression_') as folder:
  root=Path(folder)
  for rotation in (0,90,180,270):
   d=fitz.open();p=d.new_page(width=300,height=300)
   p.insert_text((50,100),'RURAL',fontname='helv',fontsize=8,render_mode=3)
   assert typography.digital(p,p.rect) is None
   p.set_rotation(rotation);source=root/('source'+str(rotation)+'.pdf');d.save(source);d.close()
   req=dict(input=str(source),output=str(root/'edited.pdf'),action='edit',pages=[0],rect=[.3,.3,.2,.035],text='CUSCO',font_size=8.5,font_family='Times New Roman',bold=True,protect_header=False)
   engine.mutate(req)
   with fitz.open(req['output']) as check:
    assert check[0].search_for('CUSCO')
    assert any(t['type']!=3 and ''.join(chr(c[0]) for c in t['chars'])=='CUSCO' for t in check[0].get_texttrace())
  assert typography.insertion_font(dict(font_family='Arial'))['fontname']!=typography.insertion_font(dict(font_family='Times New Roman'))['fontname']
 print('PASS: tight single-line edits at all rotations, invisible OCR ignored, distinct font resources')
if __name__=='__main__':
 if len(sys.argv)>1:tempfile.tempdir=str(Path(sys.argv[1]).resolve())
 run()
