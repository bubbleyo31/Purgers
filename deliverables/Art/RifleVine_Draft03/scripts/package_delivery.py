"""建立可離線檢視的交付頁、成本清單、ZIP 與完整性報告。"""
import os,json,struct,hashlib,zipfile,re,html
from pathlib import Path
BASE=Path(__file__).resolve().parent.parent
def read(name):return json.loads((BASE/name).read_text(encoding='utf-8-sig'))
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
build=read('build_report.json');validation=read('roundtrip_validation.json')
assert validation['passed'],validation['errors']
meshes=validation['output_meshes']
def costs(items):
    return {key:sum(m[key] for m in items) for key in ['vertices','quads','triangle_faces','ngons','triangulated_triangles','materials']}
textures=[]
for p in sorted((BASE/'Textures').glob('*.png')):
    with p.open('rb') as f:header=f.read(24)
    w,h=struct.unpack('>II',header[16:24])
    textures.append({'name':p.name,'width':w,'height':h,'bytes':p.stat().st_size})
resource={'baseline':build['baseline'],'full':build['full'],'new_parts':build['new_parts'],'optic':build['optic'],
'exported_full':costs(meshes),'arms':costs([m for m in meshes if m['name']=='立方体.004' or m['name'].startswith('Detail_arms_')]),
'weapon':costs([m for m in meshes if m['name']!='立方体.004' and not m['name'].startswith('Detail_arms_')]),
'meshes':meshes,'texture_files':textures,'texture_png_bytes':sum(t['bytes'] for t in textures),
'triangle_reduction_percent':round(100*(1-build['full']['triangulated_triangles']/build['baseline']['triangles']),2),
'optic_reduction_percent':round(100*(1-build['optic']['triangulated_triangles']/build['baseline']['optic_triangles']),2),
'note':'Authoring polygons are not GPU primitives. Material slot sums are not measured Unity draw calls. Arms were inherited unchanged.'}
(BASE/'resource_report.json').write_text(json.dumps(resource,ensure_ascii=False,indent=2),encoding='utf8')
panels=[
('02_Rifle_ThreeQuarter.png','整槍｜實際模型渲染'),
('01_Rifle_Side.png','側面｜圓弧後托與短彈匣'),
('03_Optic_Closeup.png','原創短筒瞄具與青綠鏡片'),
('04_Stock_Magazine.png','後托／彈匣近照'),
('05_Opposite.png','另一側'),
('06_NewParts_QuadWire.png','新增部件四角面線框'),
('08_ADS_Axis.png','光軸／透明鏡片格線檢查'),
('07_FirstPerson.png','第一人稱參考視角'),
('00_Hero.png','原手部與動作保留'),
]
figures=''.join('<figure><a href="Previews/'+p+'"><img loading="lazy" src="Previews/'+p+'" alt="'+title+'"></a><figcaption>'+title+'</figcaption></figure>' for p,title in panels)
page='''<!doctype html><html lang="zh-Hant"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Attack Rifle Concept02 模型交付</title>
<style>
:root{color-scheme:dark}body{font:16px/1.7 system-ui,"Microsoft JhengHei",sans-serif;color:#e4e5df;background:#191f23;margin:0}
main{max-width:1440px;margin:auto;padding:40px 24px}h1{font-size:36px;letter-spacing:.04em;margin:4px 0}
.tag{color:#9dc4ad;font-size:13px;letter-spacing:.14em}.intro{max-width:950px;color:#bfc9c4}
a{color:#a6d5b9}nav{display:flex;gap:20px;flex-wrap:wrap;padding:16px 0}
.stats{display:flex;gap:12px;flex-wrap:wrap;margin:24px 0}.stats>div{background:#26312f;padding:14px 24px;border-radius:8px}
.stats b{display:block;font-size:28px;color:#dbe9d6}.stats span{color:#b1c2b7;font-size:14px}
.grid{display:grid;grid-template-columns:1fr 1fr;gap:22px}figure{margin:0;background:#222a2e;border:1px solid #3c4948;border-radius:8px;overflow:hidden}
figure:first-child{grid-column:1/-1}img,video{display:block;width:100%;height:auto}figcaption{padding:12px 16px;color:#cad8cf}
.note{padding:18px 22px;border-left:3px solid #89b29b;background:#252d30;margin:24px 0}
.reference{margin-top:36px;max-width:1050px}.reference h2{font-size:22px}
@media(max-width:750px){.grid{grid-template-columns:1fr}main{padding:22px 12px}h1{font-size:28px}}
</style><main><div class="tag">PURGERS / ATTACK / DRAFT03 / 2026-10-07</div>
<h1>Concept 02 — 模型實作</h1><p class="intro">原創短筒瞄具、青綠透明鏡片、圓弧木托與短聚合物彈匣。下方圖片與影片全部來自本交付的 Blender 模型，底部另列已確認的生成概念圖。</p>
<nav><a href="RifleVine_Arms_Draft03.fbx">完整 FBX</a><a href="RifleVine_Arms_Draft03.blend">Blender 原檔</a><a href="Prism_Concept02_Module.fbx">獨立瞄具</a><a href="README.md">匯入與材質說明</a><a href="resource_report.json">完整資源統計</a><a href="roundtrip_validation.json">FBX 驗證</a></nav>
<div class="stats"><div><b>55,748</b><span>完整三角化成本 / −17.7%</span></div><div><b>3,240</b><span>新增四角面 / 零新增三角面及 N-gon</span></div><div><b>63 / 6</b><span>保留骨骼 / 動作</span></div><div><b>2,864</b><span>瞄具三角化成本 / −82.1%</span></div></div>
<div class="grid">'''+figures+'''</div>
<h2>換彈｜原動作 1–63 幀</h2><video controls preload="metadata" poster="Previews/Reload_01.png" src="Previews/09_Reload.mp4"></video>
<div class="note"><strong>驗證邊界：</strong>FBX 往返、手部網格／UV／權重一致、10,395 個骨骼樣本及九條瞄準檢查線通過。尚未匯入 Unity 或替換 Scene／Prefab；遊戲內材質、ADS、全動作穿模與實機效能另行確認。舊網格仍含 20 個三角面、202 個 N-gon 及已記錄的退化面，詳見說明。</div>
<section class="reference"><h2>已確認概念圖｜方向參考</h2><p>這張是先前確認的生成概念圖，不是模型渲染。模型沿用原手臂、動作、前端幾何及安裝光軸。</p><a href="References/Approved_Concept02.png"><img src="References/Approved_Concept02.png" alt="已確認的 Concept02"></a></section></main></html>'''
(BASE/'review.html').write_text(page,encoding='utf8')
# 相對引用存在性，包含 README 與 HTML。
links=re.findall(r'(?:src|href)="([^"]+)"',page)
links+=re.findall(r'\]\(([^)]+)\)',(BASE/'README.md').read_text(encoding='utf-8-sig'))
missing=[link for link in links if not link.startswith(('http:','https:','#')) and not (BASE/link.split('#')[0]).exists()]
assert not missing,missing
# 所有影片編碼完成才包裝。
video=BASE/'Previews/09_Reload.mp4'
assert video.stat().st_size>10000
with video.open('rb') as f:assert b'ftyp' in f.read(32)
archive=BASE/'RifleVine_Concept02_Animated_Package.zip'
files=[p for p in BASE.rglob('*') if p.is_file() and p.suffix not in {'.zip','.log'} and p.name!='delivery_validation.json']
with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for p in files:z.write(p,p.relative_to(BASE).as_posix())
with zipfile.ZipFile(archive) as z:
    bad=z.testzip();assert bad is None,bad
    assert hashlib.sha256(z.read('RifleVine_Arms_Draft03.fbx')).hexdigest()==sha(BASE/'RifleVine_Arms_Draft03.fbx')
report={'passed':True,'archive':archive.name,'file_count':len(files),'zip_bytes':archive.stat().st_size,'bad_zip_member':bad,
'missing_links':missing,'fbx_sha256':sha(BASE/'RifleVine_Arms_Draft03.fbx'),'blend_sha256':sha(BASE/'RifleVine_Arms_Draft03.blend'),
'video_bytes':video.stat().st_size,'scope':'ZIP integrity, local links and FBX hash. Unity import remains unverified.'}
(BASE/'delivery_validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(report,ensure_ascii=False))

