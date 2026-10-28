"""Run the portable, generated-fixture regression suite and write a report."""
import argparse,importlib,sys,tempfile,json,traceback,time
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
parser=argparse.ArgumentParser()
parser.add_argument('--scratch',required=True)
parser.add_argument('--output',required=True)
parser.add_argument('--ocr')
args=parser.parse_args()
folder=Path(args.scratch).resolve();folder.mkdir(parents=True,exist_ok=True);tempfile.tempdir=str(folder)
report={'version':'1.1.6','suites':[]}
for name in ['test_engine','test_edit_regression','test_gap','test_height','test_tone','test_paper']+(['test_typography'] if args.ocr else []):
 start=time.monotonic()
 try:
  module=importlib.import_module(name)
  detail=module.test_all() if name=='test_engine' else module.run(Path(args.ocr).resolve()) if name=='test_typography' else module.run()
  entry={'suite':name,'status':'PASS','seconds':round(time.monotonic()-start,2)}
  if detail is not None:entry['details']=detail
 except Exception:
  entry={'suite':name,'status':'FAIL','error':traceback.format_exc(),'seconds':round(time.monotonic()-start,2)}
 report['suites'].append(entry)
 print(name+': '+entry['status'],flush=True)
report['passed']=sum(s['status']=='PASS' for s in report['suites']);report['failed']=len(report['suites'])-report['passed']
Path(args.output).write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
sys.exit(1 if report['failed'] else 0)
