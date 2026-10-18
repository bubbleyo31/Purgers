from pathlib import Path
import hashlib,json,re
from html.parser import HTMLParser
R=Path('M:/UnityProject/Purgers');O=R/'deliverables/UIAudit_20261018'
before=json.loads((O/'protected-hashes-before.json').read_text('utf-8'));changed=[];missing=[]
for s,h in before.items():
 p=R/s
 if not p.exists():missing.append(s)
 elif hashlib.sha256(p.read_bytes()).hexdigest()!=h:changed.append(s)
class Check(HTMLParser):
 def __init__(self):super().__init__();self.links=[]
 def handle_starttag(self,t,attrs):
  a=dict(attrs)
  if t in ['a','img','iframe']:
   u=a.get('href',a.get('src',''))
   if u and not u.startswith(('http','#','data:')):self.links.append(u)
c=Check();c.feed((O/'report.html').read_text('utf-8'));bad=[x for x in c.links if not (O/x.split('#')[0]).exists() and x!='audit-integrity.json']
out={'protected_file_count':len(before),'changed':changed,'missing':missing,'report_broken_local_links':bad,'note':'Hashes compare audit-start against audit-end on selected source/scene/UI/presentation folders; not a whole-repository or build validation.'};(O/'audit-integrity.json').write_text(json.dumps(out,ensure_ascii=False,indent=2),'utf-8');print(json.dumps(out,ensure_ascii=False))
