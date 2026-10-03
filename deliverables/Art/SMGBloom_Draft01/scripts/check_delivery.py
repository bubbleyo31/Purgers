import json,re,hashlib
from pathlib import Path
base=Path(r'M:/UnityProject/Purgers/deliverables/Art/SMGBloom_Draft01')
links=re.findall(r'(?:href|src)="([^"]+)"',(base/'review.html').read_text(encoding='utf-8-sig'))
links += re.findall(r'\]\(([^)]+)\)',(base/'README.md').read_text(encoding='utf-8-sig'))
missing=[p for p in links if not p.startswith(('https://','http://','#')) and not (base/p).exists()]
report={'missing_review_links':missing,'source_unchanged':hashlib.sha256(Path(r'M:/UnityProject/Purgers/Assets/_Project_Assets/Models/Player/Profession/Support/Materials/mp7/第一人稱_衝鋒槍_uv.fbx').read_bytes()).hexdigest()==json.loads((base/'build_report.json').read_text())['source_sha256'],'final_model_sha256':hashlib.sha256((base/'SMGBloom_Arms_Draft01.fbx').read_bytes()).hexdigest(),'lens_alpha_roundtrip':next(m['alpha'] for m in json.loads((base/'optic_roundtrip.json').read_text())['materials'] if m['name']=='Reflex_Lens_Coating'),'source_unity_readonly_inspection':{'animation_type':'Generic','global_scale':1,'non_preview_clip_count':5,'skinned_renderer_count':15,'frame_rate':24,'compilation_failed':False},'output_unity_imported':False,'scene_prefab_changed_by_task':False}
(base/'delivery_validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(report,ensure_ascii=False));assert not missing and report['source_unchanged']
