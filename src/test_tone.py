import sys,tempfile
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import fitz,engine

def run():
 with engine.scratch('tone_test_') as folder:
  root=Path(folder)
  for color in ((.35,.35,.35),(.1,.2,.55)):
   d=fitz.open();p=d.new_page(width=300,height=200)
   p.insert_text((40,100),'Original text',fontsize=12,color=color)
   assert engine.scan_appearance(p,p.rect) is None
   qdoc=fitz.open();q=qdoc.new_page(width=300,height=200);q.insert_image(q.rect,stream=p.get_pixmap(dpi=300).tobytes('png'))
   appearance=engine.scan_appearance(q,fitz.Rect(38,85,120,105))
   assert appearance and max(abs(a-b) for a,b in zip(appearance['color'],color))<.07,appearance
   assert engine.scan_appearance(q,fitz.Rect(30,130,130,160)) is None
   source=root/'source.pdf';out=root/'out.pdf';qdoc.save(source);qdoc.close()
   for gap in (False,True):
    engine.mutate(dict(input=str(source),output=str(out),action='edit',pages=[0],rect=[38/300,85/200,82/300,20/200],text='New text',font_size=12,font_family='Arial',match_ink=True,close_gap=gap,protect_header=False))
    with fitz.open(out) as check:
     assert check[0].search_for('New text')
     assert len(check[0].get_images())>=2
     assert any(t['type']==3 and 'New text'==''.join(chr(c[0]) for c in t['chars']).replace(chr(160),' ') for t in check[0].get_texttrace())
 print('PASS: gray and blue ink, paper ignored, digital text unchanged, searchable scan replacement with both spacing modes')
if __name__=='__main__':
 if len(sys.argv)>1:tempfile.tempdir=str(Path(sys.argv[1]).resolve())
 run()
