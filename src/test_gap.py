import sys,tempfile
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import fitz,engine

def run():
 with engine.scratch('gap_test_') as folder:
  root=Path(folder)
  for rotation in (0,90,180,270):
   original=fitz.open();p=original.new_page(width=600,height=300)
   p.insert_text((40,100),'Por medio de la presente, comunico a ustedes',fontsize=12,fontname='helv')
   p.insert_text((40,130),'Esta segunda linea debe conservarse',fontsize=12,fontname='helv')
   image=p.get_pixmap(dpi=300)
   scan=fitz.open();q=scan.new_page(width=600,height=300);q.insert_image(q.rect,stream=image.tobytes('png'))
   # Counterrotate source pixels so each displayed test line remains horizontal.
   if rotation:
    rotated=fitz.open();z=rotated.new_page(width=300 if rotation in (90,270) else 600,height=600 if rotation in (90,270) else 300)
    z.insert_image(z.rect,stream=image.tobytes('png'),rotate=rotation,keep_proportion=False);z.set_rotation(rotation);scan=rotated;q=z
   source=root/('source'+str(rotation)+'.pdf');scan.save(source)
   req=dict(input=str(source),output=str(root/('out'+str(rotation)+'.pdf')),action='edit',pages=[0],rect=[39/600,88/300,59/600,15/300],font_size=12,font_family='Arial',text='la cual',close_gap=True,match_ink=True,protect_header=False)
   engine.mutate(req)
   with fitz.open(req['output']) as check:
    assert check[0].search_for('la cual')
    before=q.get_pixmap(dpi=144,clip=fitz.Rect(35,115,400,140))
    after=check[0].get_pixmap(dpi=144,clip=fitz.Rect(35,115,400,140))
    assert before.samples==after.samples,rotation
  # Oversized or multiline replacements must fail without writing a partial PDF.
  for text in ('X'*500,'first\nsecond'):
   req['text']=text;req['output']=str(root/'rejected.pdf')
   try:engine.mutate(req)
   except ValueError:pass
   else:raise AssertionError('unsafe layout accepted')
   assert not Path(req['output']).exists()
 print('PASS: close gaps at all rotations, neighboring line unchanged, overflow and multiline rejected')
if __name__=='__main__':
 if len(sys.argv)>1:tempfile.tempdir=str(Path(sys.argv[1]).resolve())
 run()
