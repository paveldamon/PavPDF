import sys,tempfile
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import fitz,engine

def run():
 with engine.scratch('fit_test_') as folder:
  root=Path(folder);d=fitz.open();p=d.new_page(width=400,height=250)
  p.insert_text((40,100),'Original text followed by words',fontsize=10)
  p.insert_text((40,125),'Neighbor must remain unchanged',fontsize=10)
  image=p.get_pixmap(dpi=300);scan=fitz.open();q=scan.new_page(width=400,height=250);q.insert_image(q.rect,stream=image.tobytes('png'))
  source=root/'source.pdf';scan.save(source)
  req=dict(input=str(source),output=str(root/'out.pdf'),action='edit',pages=[0],rect=[39/400,91/250,56/400,12/250],text='Nuevo texto',font_family='Arial',font_size=24,close_gap=True,match_ink=True,protect_header=False)
  try:engine.mutate(req)
  except ValueError:pass
  else:raise AssertionError('Expected oversized font failure without fitting')
  req['auto_fit']=True;result=engine.mutate(req)
  assert 4<=result['font_sizes'][0]<24,result
  with fitz.open(req['output']) as check:
   assert check[0].search_for('Nuevo texto')
   region=fitz.Rect(35,115,250,130)
   assert q.get_pixmap(clip=region).samples==check[0].get_pixmap(clip=region).samples
  req['close_gap']=False;result=engine.mutate(req);assert 4<=result['font_sizes'][0]<24
 print('PASS: oversized font adapts in both modes; neighboring line preserved; applied size reported')
if __name__=='__main__':
 if len(sys.argv)>1:tempfile.tempdir=str(Path(sys.argv[1]).resolve())
 run()
