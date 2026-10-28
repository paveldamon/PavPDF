import sys,tempfile,io,random
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import engine,fitz
from PIL import Image,ImageStat

def run():
 with engine.scratch('paper_test_') as folder:
  root=Path(folder)
  for base in ((225,225,225),(230,220,205)):
   rng=random.Random(9);im=Image.new('RGB',(1200,800));im.putdata([tuple(max(0,min(255,c+rng.randrange(-8,9))) for c in base) for _ in range(1200*800)])
   buf=io.BytesIO();im.save(buf,format='PNG');d=fitz.open();p=d.new_page(width=300,height=200);p.insert_image(p.rect,stream=buf.getvalue());p.insert_text((40,100),'Original text followed by words',fontsize=10)
   rect=fitz.Rect(39,90,98,103);patch=engine.paper_patch(p,rect);assert patch
   stats=ImageStat.Stat(Image.open(io.BytesIO(patch)))
   assert all(abs(a-b)<4 for a,b in zip(stats.mean,base)),stats.mean
   assert min(stats.stddev)>1,'Paper grain lost'
   source=root/'source.pdf';d.save(source)
   for gap in (False,True):
    out=root/'out.pdf';engine.mutate(dict(input=str(source),output=str(out),action='edit',pages=[0],rect=[39/300,90/200,59/300,13/200],text='Nuevo texto',font_size=10,font_family='Arial',close_gap=gap,match_paper=True,match_ink=True,auto_fit=True,protect_header=False))
    with fitz.open(out) as check:
     assert check[0].search_for('Nuevo texto')
     assert check[0].get_pixmap(clip=fitz.Rect(0,0,300,60)).samples==p.get_pixmap(clip=fitz.Rect(0,0,300,60)).samples
 print('PASS: gray/beige paper tone and grain, both edit modes, searchable text, header unchanged')
if __name__=='__main__':
 if len(sys.argv)>1:tempfile.tempdir=str(Path(sys.argv[1]).resolve())
 run()
