from pathlib import Path
import zipfile,hashlib,json,re
base=Path(__file__).resolve().parent.parent;archive=base/'Chainsaw_ThreeView_Animated_Package.zip'
files=[base/n for n in ['Chainsaw_ThreeView_Animated.fbx','IMPORT_README.txt','build_report.json','roundtrip_validation.json','delivery_validation.json','construction_report.json','grip_clearance.json','relief_report.json','reference_extraction.json']]
for d in ['Chainsaw_ThreeView_Animated.fbm','Textures']:
 files.extend(p for p in (base/d).rglob('*') if p.is_file())
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=3) as z:
 for p in files:z.write(p,p.relative_to(base).as_posix())
with zipfile.ZipFile(archive) as z:
 corrupt=z.testzip();exact=hashlib.sha256(z.read('Chainsaw_ThreeView_Animated.fbx')).hexdigest()==hashlib.sha256((base/'Chainsaw_ThreeView_Animated.fbx').read_bytes()).hexdigest()
links=re.findall(r'(?:href|src)="([^"]+)"',(base/'review.html').read_text(encoding='utf-8-sig'))
links+=re.findall(r'\]\(([^)]+)\)',(base/'README.md').read_text(encoding='utf-8-sig'))
missing=[p for p in links if not p.startswith(('http','#')) and not (base/p).exists()]
report={'package':archive.name,'bytes':archive.stat().st_size,'files':len(files),'corrupt_member':corrupt,'fbx_hash_matches':exact,'missing_review_links':missing}
(base/'package_report.json').write_text(json.dumps(report,indent=2),encoding='utf8')
print(json.dumps(report));assert corrupt is None and exact and not missing
