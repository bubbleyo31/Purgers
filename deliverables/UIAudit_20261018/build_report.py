from pathlib import Path
from PIL import Image
import json,base64,io,html,collections
R=Path('M:/UnityProject/Purgers');O=R/'deliverables/UIAudit_20261018';V=Path('C:/Users/hsuai/.codex/visualizations/2026/10/17/01a14b5d-49b4-75e1-b279-5433600563a6/weapon-optical-comparison.html')
issues=json.loads((O/'findings.json').read_text('utf-8-sig'));inventory=json.loads((O/'asset-inventory.json').read_text('utf-8'));imports=json.loads((O/'imported-textures.json').read_text('utf-8-sig'))
byname=collections.defaultdict(list)
for p in (R/'Assets').rglob('*'):
 if p.is_file():byname[p.name].append(p)
for issue in issues:
 for s in issue['sources']:
  found=byname[s[0]];found=[x for x in found if '/Active/Rewards/' in x.as_posix()] if s[0]=='Blink.asset' else found;s[2:]=[found[0].relative_to(R).as_posix() if found else '']
(O/'findings.json').write_text(json.dumps(issues,ensure_ascii=False,indent=2),'utf-8')
# Exact source silhouettes at preview resolution; no source asset is changed.
before=[];after=[]
for name,label,target in [('Attack','Attack 步槍',134),('Support','Support SMG',109),('Tank','Tank 電鋸',134)]:
 p=R/f'Assets/_Project_Assets/UI/BattleHUD/WeaponIcon/{name}Weapon.png';im=Image.open(p).convert('RGBA');bb=im.getchannel('A').point(lambda a:255 if a>=128 else 0).getbbox();thumb=im.resize((256,256),Image.Resampling.LANCZOS);buff=io.BytesIO();thumb.save(buff,format='PNG');url='data:image/png;base64,'+base64.b64encode(buff.getvalue()).decode();cx=(bb[0]+bb[2])/2;cy=(bb[1]+bb[3])/2;scale=min(target/(bb[2]-bb[0]),78/(bb[3]-bb[1]));sz=im.width*scale;x=82-cx*scale;y=52-cy*scale
 before.append(f'<div class="weapon-row"><div class="weapon-box"><img src="{url}" alt="{label}原圖" style="width:104px;height:104px;left:30px;top:0"></div><span class="caption">{label}</span></div>')
 after.append(f'<div class="weapon-row"><div class="weapon-box"><img src="{url}" alt="{label}同一原圖校準" style="width:{sz:.2f}px;height:{sz:.2f}px;left:{x:.2f}px;top:{y:.2f}px"></div><span class="caption">{label}</span></div>')
frag=V.read_text('utf-8-sig').replace('@@BEFORE@@',''.join(before)).replace('@@AFTER@@',''.join(after));V.write_text(frag,'utf-8')
(O/'weapon-comparison.html').write_text('<!doctype html><html lang="zh-Hant"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>武器圖示光學校準</title><body style="margin:24px;background:#171d16;color-scheme:dark">'+frag+'</body></html>','utf-8')
def esc(s):return html.escape(str(s))
def source_link(s):
 name,line,path=s;return f'<a href="../../{esc(path)}">{esc(path)}'+(f':{line}' if line else '')+'</a>' if path else esc(name)
refs=[
('使用者指定','LoL 技能冷卻','整格徑向遮罩、置中白字、完成光與二次施放訊號；現行版本已實作。官方9.1可支持把持續狀態與再施放區分，不能用它推翻已核准樣式。','https://www.leagueoflegends.com/en-au/news/game-updates/patch-9-1-notes/'),
('先前記憶記錄，未讀回原始回合','Overwatch 2 圖示','既有記錄指向簡潔易讀的OW2風格。本輪看過官方英雄頁的Perk圖示：主形、負空間與動作方向清楚；它們不是原技能圖示。48px為本案測試尺寸，非Blizzard規定。','https://overwatch.blizzard.com/en-us/heroes/mercy/'),
('使用者指定，原對話已核對','Escape from Tarkov 觀戰','使用者認可底部中央頭像＋玩家名，Q/E切換。適用的是觀戰資訊位置與對象辨識；現有共用肖像是暫用，不能自行導入Steam或生成新角色。官方更新文支持觀戰功能，不作該社群截圖版式的證據。','https://www.escapefromtarkov.com/news/id/404'),
('使用者指定，原對話已核對','Lethal Company 護目鏡','原話「可以參考遊戲 致命公司lethal company」，後續已接受柔邊提案。只取配戴感、周邊內襯與可調厚度／透明度；不擴大遮蔽或改HUD。','../../Documentation/ProjectArchitecture/103_第一人稱護目鏡效果.md'),
('本輪補充','Destiny 2 資訊優先順序','官方UI團隊把關鍵與武器狀態分區、穩定計時位置，並因視線移動成本放棄右側新區。適合提醒不要到處新增提示。不能照抄其省略低優先buff：本案狀態列已定案不折疊不省略。','https://www.bungie.net/7/en/News/Article/twid-04-11-2024'),
('本輪補充','Riot 視覺清晰度','視覺重量應與當下重要性一致，減少噪音並保留可辨識主形。用於技能家族光學校準，不是重畫已批准人物的授權。','https://www.leagueoflegends.com/en-us/news/dev/clarity-in-league/'),
('本輪補充，歷史資料','Deep Rock Galactic 隊友狀態','合作遊戲需要持續看見隊友身分、血量、特殊困境與求助。可檢視本案救援優先序；官方歷史更新不等於目前HUD版式驗證，不複製採礦資訊。','https://www.deeprockgalactic.com/updatesold'),
('本輪補充','Apex Legends 情境訊號','官方教學用符號配合情境與操作理解。適合提示短文字、圖形與位置共同承擔語意；本案不因此新增Ping系統。','https://help.ea.com/en/articles/apex-legends/how-to-play/'),
('本輪補充，歷史設計說明','Left 4 Dead 2 血量狀態真實性','Valve討論暫時與永久血量呈現可能誤導狀態；可借鑑「畫面要反映真實可行動狀態」，不只把全部危險染紅。該文不是救援UI版式證據。','https://www.l4d.com/blog/post.php?id=4641')]
coverage=[
('主選單／多人／命名／讀檔／刪除','插畫、筆刷、視窗歷史驗圖與目前資產','保留角色與主Logo；超寬裁切、刪除焦點／取消恢復待自然驗收'),
('設定／ESC／改鍵','最新來源、舊配色驗圖、鍵名映射','設定不用筆刷是明確例外；舊圖四模式不能當成現行缺第五模式'),
('右下技能／冷卻','最新來源與六狀態正式預覽','新徑向CD與一次完成光保留；小字、長鍵名與亮底待驗'),
('右下武器／彈量／換彈','源圖、使用端、場景序列化','內容留白可校準；換裝動態及自然背景待驗'),
('生命／護盾／能量／受傷方向','規則與歷史驗圖','保留整格血盾及淡金盾；多事件共同可見性待驗'),
('成長／獎勵卡／階級','全部成長源圖、匯入、各階級驗圖、文案','完整圖示與粗襯線保留；720p閱讀與選卡干擾待驗'),
('TAB／tooltip','最新實作、1920與3840離屏正式元件','已重現32:9裁切；15項溢出；真實12人、長名與hover未自然驗證'),
('安全屋／投票／小地圖','最新規則、批准第一版與Unity預覽','保留深橄欖與切角；不要拿舊藍色原型當現況'),
('遊戲小地圖／探索／掉落','現行責任與設定','密集掉落、同點隊友、高低差、TAB共存待自然驗收'),
('怪物／隊友／自己狀態列','正式元件來源、原圖、原生尺寸合成','保留圖示＋量條、無文字、全數直列；16px辨識列風險'),
('救援／受救援／自救','改鍵路徑、分支、歷史場景驗圖','自救與他救交接、多救援者、未配置提示待驗'),
('觀戰／死亡／全滅','原使用者對話、Provider、正式預覽與規則','固定Q/E為已批准例外；自然完整轉場、慢Client待驗'),
('護目鏡／第一人稱覆蓋','Lethal Company原對話與已核准圖','HUD下方層級保留；最大厚度＋受傷＋狀態共存待驗'),
('世界掉落／互動文字','當前來源與最新粗體驗圖','硬寫E確證；多個物件重疊與亮背景待驗'),
('聊天／加入退出訊息','LocalChatPanel來源','關rich text為既有安全策略；長名字／多行位置待驗；停用聊天輸入不是缺陷'),
('連線Loading／黑幕／關房','目前排序來源與已完成驗證紀錄','32766 Loading在32767淡黑下；自然取消／慢連線／跨程序關房待驗'),
('2D槍口／煙霧／火花／爆炸','24張自有路徑VFX來源總覽與部分材質引用','4×3邊界資料問題；逐幀、明暗、過繪與粒子成本未量測'),
('彈孔貼花','4張原圖、正式三材質與匯入差異','遠近斜面堆疊待驗；未引用03不擅刪'),
('字型／匯入／素材生命週期','102張實際Unity匯入查詢、兩套動態字型','性能不是PNG檔案大小；Build常駐／缺字／Atlas相容待驗'),
('其餘3D材質與第三方UI','521項全量圖檔清單與分類','未逐張宣稱3D材質美感合格；第三方示例／歷史素材不等同現役畫面')]
css='''body{margin:0;background:#141a13;color:#ece9dc;font:17px/1.8 "Microsoft JhengHei",Arial,sans-serif}main{max-width:1160px;margin:auto;padding:48px 28px}h1{font-size:38px;line-height:1.35}h2{margin-top:56px;color:#dfcc7e;font-size:26px}h3{font-size:21px;margin:0 0 12px}p{margin:12px 0}a{color:#dac880;overflow-wrap:anywhere}nav{display:flex;gap:20px;flex-wrap:wrap;padding:18px 0;border-top:1px solid #48513c;border-bottom:1px solid #48513c}.lead{font-size:21px}.meta{color:#b7c0a7}.item{padding:28px 0;border-top:1px solid #46503a}.tag{color:#dac880}.item dl{display:grid;grid-template-columns:85px 1fr;gap:10px;margin:16px 0}.item dd{margin:0}.src{font-size:13px;line-height:1.7}figure{margin:28px 0}img{max-width:100%;height:auto}figcaption{font-size:14px;color:#b7c0a7}table{width:100%;border-collapse:collapse;font-size:15px}td,th{padding:12px;text-align:left;vertical-align:top;border-bottom:1px solid #3f4935}th{color:#dfcc7e}td:first-child{min-width:110px}.scroll{overflow-x:auto}.note{background:#242c1d;padding:20px}.twocol{display:grid;grid-template-columns:1fr 1fr;gap:20px}input{font:inherit;padding:12px;background:#242c1d;color:#ece9dc;border:1px solid #68764e;width:min(90%,600px)}details{margin:14px 0}summary{cursor:pointer;color:#dfcc7e}@media(max-width:700px){iframe[title="武器圖示對比"]{height:1030px!important}main{padding:24px 16px}.twocol{grid-template-columns:1fr}h1{font-size:29px}.item dl{display:block}.item dt{color:#b7c0a7}.item dd{margin-bottom:12px}}'''
parts=['<!doctype html><html lang="zh-Hant"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Purgers UI與2D美術審查 2026-10-18</title><style>'+css+'</style></head><body><main>','<p class="tag">PURGERS · UI / 2D ART REVIEW · 2026-10-18</p><h1>保留既有美術，優先修正資訊錯誤與顯示邊界</h1><p class="lead">結論不是推翻整套UI。最有價值的下一步，是讓同一個操作、同一個狀態、同一張圖，在不同使用端都被一致而可靠地看懂。</p><p class="meta">唯讀審查＋獨立提案。等待另外兩個工作完成後才開始檢查。三個監督角色分別查結構、資產、參考，再交叉反駁；沒有修改正式Assets或接管前景。</p>','<nav><a href="#summary">結論</a><a href="#compare">生成對照</a><a href="#findings">22項發現</a><a href="#direction">美術方向</a><a href="#references">遊戲參考</a><a href="#coverage">覆蓋範圍</a><a href="#acceptance">修正與驗收</a><a href="#inventory">素材清單</a></nav>']
parts+=['<h2 id="summary">先修什麼，先保留什麼</h2><ol><li><b>第一順位：</b>舊瞄準文案、改鍵後還寫E，直接誤導操作；成本低、回報最高。</li><li><b>第二順位：</b>TAB 32:9縮放、15成長溢出提示、真實總人數；維持四列規格。</li><li><b>第三順位：</b>長鍵名、小字、原生尺寸圖示與右下武器光學校準；不要靠大幅重畫解決。</li><li><b>量測後再決定：</b>1024技能／1254武器貼圖、動態字型、槍口序列邊界；未證實性能瓶頸不直接壓縮全專案。</li></ol><p>深橄欖、米白、淡金、切角、筆刷和完整剪影已形成辨識度。主選單可以偏插畫和材質，戰鬥HUD則應把視覺重量留給血量、操作與短暫危險。設定／ESC不用筆刷、狀態列無文字，都是明確例外，不能為了形式統一而抹平。</p><div class="note">證據分層：確證＝目前來源／資料；離屏＝正式元件＋示例資料渲染；風險＝可推導但待原生／自然情境；審美＝有理由的設計取捨。沒有把22項全部列成現行bug。</div>']
parts+=['<h2 id="compare">已生成的改進對照</h2><p>下圖左右使用同一套正式TAB元件與真實Sprite。只在暫存preview scene中改縮放，增加15項溢出提示。為驗證解析度，依正式CanvasScaler公式套入離屏尺寸；它不是改變Game View後的自然遊玩截圖。</p><figure><a href="tab-ultrawide-comparison.png"><img src="tab-ultrawide-comparison.png" alt="TAB 32比9現況裁切與提案完整顯示"></a><figcaption>左：現行比例；右：依可用高度縮放。中央1800px區域縮小對照。<a href="tab-current-3840.png">現況完整3840</a> · <a href="tab-proposed-3840.png">提案完整3840</a></figcaption></figure><div class="twocol"><figure><a href="tab-current-1920.png"><img src="tab-current-1920.png" alt="1080p目前TAB15項"></a><figcaption>1920×1080目前：第三列15項，沒有提示下方還有內容。</figcaption></figure><figure><a href="tab-proposed-1920.png"><img src="tab-proposed-1920.png" alt="1080p提案成長數量與捲軸"></a><figcaption>提案：保留四列與圖示，只在溢出時顯示總數／細捲軸。這裡是外觀驗證，尚未驗拖曳或懸停。</figcaption></figure></div><h3>武器顯示校準，不重畫武器</h3><p>以下是標準診斷框的顯示原理，並非目前HUD尺寸的逐像素複製。提案保留短槍／長槍差異，調整可見範圍與基線；數值需回到原HUD驗收。</p><iframe src="weapon-comparison.html" title="武器圖示對比" style="width:100%;height:530px;border:0"></iframe><p><a href="weapon-comparison.html">獨立開啟可切底色對照</a></p><h3>透明與原生尺寸診斷</h3><figure><a href="alpha-native-size-review.png"><img src="alpha-native-size-review.png" alt="七張原圖在三底色四尺寸合成"></a><figcaption>16／24／48／96px來源縮圖，未清理alpha。白色Growth在米白底的低對比不代表正式缺陷：正式UI會染深色。這張圖把「低alpha很多」降級為待實際使用端確認，沒有藉此重製圖示。</figcaption></figure>']
parts+=['<h2 id="findings">逐項發現與可行修法</h2>']
for i in issues:
 parts.append(f'<article class="item" id="{i["id"]}"><p class="tag">{i["id"]} · {i["priority"]} · {esc(i["kind"])}</p><h3>{esc(i["title"])}</h3><dl><dt>證據／判斷</dt><dd>{esc(i["evidence"])}</dd><dt>如何修正</dt><dd>{esc(i["fix"])}</dd><dt>驗收</dt><dd>{esc(i["verify"])}</dd></dl><p class="src">'+ '<br>'.join(source_link(s) for s in i['sources'])+'</p></article>')
parts+=['<h2 id="direction">美感方向：從「同一個世界」走向「同一套閱讀規則」</h2><p><b>保留世界個性：</b>主選單的角色、工具、藤葉與破損材質，適合承擔世界觀。讓筆刷主要服務選單選取／主要動作，讓戰鬥框架保持較穩定的邊緣和負空間。不要將每個卡片都加污漬、描邊、發光，否則所有訊息會同時爭取注意。</p><p><b>建立視覺重量順序：</b>立即危險／需要現在操作 → 核心資源／目前目標 → 暫時效果 → 查閱資訊。這是呈現優先序，不是刪除既有狀態或新增一套HUD。持續效果的光不要與一次冷卻完成光同樣強；金／青狀態應與文字或輪廓一起理解，而不是只看色相。</p><p><b>字級與對齊：</b>數字固定位置，名稱長度不推擠狀態；常用鍵帽短名稱可讀，完整名稱留在設定。每個容器先決定內距、基線與最大文字區，才調個別素材。不要以縮字作為所有極端內容的唯一解法。</p><p><b>圖示家族：</b>單色能力維持主輪廓；狀態彩色圖維持已批准意義。兩者可共用光學尺寸、基線和背景保護，而不必全改成同一種畫法。羅馬級數保持粗襯線與每項中心設定。任何「精修」先過原尺寸閱讀，再比較放大美感。</p><p><b>共用頭像：</b>暫用人形沒有足夠資訊，不值得與血量、姓名爭相同重量。未有正式肖像之前，可以改善姓名／隊伍序號的穩定位置；不能為了漂亮先生成四個不存在的人物。</p><p><b>主選單：</b>目前插畫、米白Logo與黃綠選取筆刷有連結。下一步應驗寬高比安全區、按鈕hover／disabled、對話框閱讀順序；不是立刻換成通用科幻玻璃面板。</p>']
parts+=['<h2 id="references">遊戲參考與適用邊界</h2><p>查找以AnySearch開始，部分官方頁面另以網頁工具核對。下列歷史設計文章用來分析方法，不冒稱是2026最新版本UI規範。外部遊戲只支撐原理；專案是否有問題仍以本輪證據為準。</p>']
for status,name,reason,url in refs:parts.append(f'<article class="item"><p class="tag">{esc(status)}</p><h3>{esc(name)}</h3><p>{esc(reason)}</p><a href="{esc(url)}">來源／正式記錄</a></article>')
parts+=['<h2 id="coverage">檢查覆蓋與未驗範圍</h2><p>521個圖像／向量來源已列入清單；其中82張專案UI、11張專案Image、15張_Project_Assets/VFX，另有9張Art/VFX，共24張這兩個路徑下的VFX／貼花來源。32個other_sprite包含第三方／其他素材；381個other_texture主要含3D材質等。分類為路徑／Importer規則，不等於現役Build依賴。</p><div class="scroll"><table><thead><tr><th>表面</th><th>本輪證據</th><th>限制／待驗</th></tr></thead><tbody>']
for row in coverage:parts.append('<tr>'+''.join('<td>'+esc(c)+'</td>' for c in row)+'</tr>')
parts+=['</tbody></table></div><figure><a href="reviewed-ui-evidence.jpg"><img src="reviewed-ui-evidence.jpg" alt="十二個既有驗證畫面總覽"></a><figcaption>已閱讀的歷史驗圖總覽；混合不同時間的證據，尤其護目鏡圖內藍色HUD為較早版本，不能當成目前配色。</figcaption></figure><details><summary>素材縮圖總覽</summary>']
for p in sorted(O.glob('contact-*.jpg')):parts.append(f'<figure><a href="{p.name}"><img src="{p.name}" alt="{p.stem}"></a><figcaption>{p.name}。縮圖標籤的max為.meta首欄原始抽取，可能是過期／重複值；請以本輪Unity匯入清單為準。</figcaption></figure>')
parts+=['</details><h2 id="acceptance">修正順序與驗收門檻</h2><table><tr><th>階段</th><th>工作切片</th><th>通過依據</th></tr><tr><td>先修事實</td><td>UI-01／02；單一綁定文案來源</td><td>顯示文案、改鍵、保存載入一致；有行為變更則先失敗測試，再最小修正</td></tr><tr><td>補邊界</td><td>UI-03／05／06／08／09；UI-04先修真實總數</td><td>各解析度、長名、極長鍵、15／64項、tooltip四角及游標命中</td></tr><tr><td>精修視覺</td><td>ART-01／02／04／UI-07</td><td>原尺寸閱讀、保留原圖、各狀態、明暗背景；使用者視覺接受另列</td></tr><tr><td>量測資源</td><td>ART-06／07、VFX-01</td><td>Memory Profiler、實際Build平台、逐幀播放及原圖對照；不以來源檔大小替代</td></tr></table><p>真正實作時仍依專案流程：Unity編譯／Console、focused test、非零PurgersRegression、相關Host／Client與late join／reconnect分開驗。此次是唯讀美術審查，沒有重跑整套回歸；前兩個工作的既有4個回歸失敗不歸因於本次審查，也不能說全專案已全綠。</p><p>本輪沒有進入自然Play Mode、搶焦點或操作使用者輸入。離屏元件、靜態來源、歷史驗圖與自然遊玩是不同證據；沒有宣稱全部UI已驗收完成。</p><h3>監督者成功攔下的誤判</h3><ul><li>成長圖已256，不能把.meta第一個2048當最終匯入；MenuV2也不是8K runtime。</li><li>RankBackground mipmap／Trilinear是刻意校準，不是漏改。</li><li>TAB已trim武器、已可捲動、正文已自動增高；只報剩餘缺口。</li><li>觀戰Q/E固定鍵、設定不套筆刷、狀態列無文字都已批准。</li><li>半透明像素不等於髒邊；分數像素格不等於已看見閃爍。</li><li>同源字型不等於必須合併；Atlas不保證降低draw call且有packed相容風險。</li><li>同頭像是暫用，不能憑空生成新人物來偽裝功能完成。</li></ul><h3>索引候選條目（未寫入正式索引）</h3><p>建議候選關鍵詞：UI-AUDIT、BINDING-COPY、UI-SAFE-AREA、OPTICAL-ICON-SCALE、SMALL-SIZE-READABILITY。內容可路由到既有104設定、106隊伍資訊、107技能狀態、166主選單美術、105異常狀態、167已確認HUD美術與整格血量及168角色成長被動與技能掉落文件。此次僅提出具體候選，不將提案冒充定案，也不向睡眠中的使用者提問。</p>']
parts+=['<h2 id="inventory">完整盤點與可追查資料</h2><p>結尾雜湊核對：1,692個受保護的程式／場景／UI／呈現檔案全部未變，未遺失；_Menu仍乾淨、非Play、非編譯。報告桌面／窄螢幕檢查無橫向溢出，篩選與底色切換正常，未捕獲JavaScript錯誤。</p><p><a href="asset-inventory.json">521項來源圖清單</a> · <a href="imported-textures.json">102項Unity實際匯入資料</a> · <a href="findings.json">22項結構化發現</a> · <a href="audit-integrity.json">受保護檔案核對</a></p><p>來源檔案總量約672.3MiB，包含未使用、第三方與3D材質；不是Build大小或GPU記憶體。102張匯入查詢為工具逐張載入後的Editor快照，其Profiler記錄不代表正常遊玩常駐；分析以width／height／format為主。</p><label for="asset-filter">依檔名／路徑篩選</label><br><input id="asset-filter" placeholder="例如 Weapon、MenuV2、Status"><p id="asset-count" class="meta">521項</p><div class="scroll"><table id="asset-table"><thead><tr><th>來源路徑</th><th>來源尺寸</th><th>Unity匯入</th><th>分類</th></tr></thead><tbody>']
imp={x['path']:x for x in imports}
for x in inventory:
 y=imp.get(x['path']);parts.append('<tr>'+f'<td>{esc(x["path"])}</td><td>{x.get("width","?")} × {x.get("height","?")}</td><td>'+ (f'{y["width"]} × {y["height"]} / {y["format"]}' if y else '未本輪載入查詢')+f'</td><td>{x["group"]}</td></tr>')
parts+=['</tbody></table></div><p class="meta">報告與生成物位於 deliverables/UIAudit_20261018。正式實作尚未套用，視覺提案尚未獲使用者接受。</p></main><script>const filter=document.getElementById("asset-filter");filter.addEventListener("input",()=>{let n=0;document.querySelectorAll("#asset-table tbody tr").forEach(r=>{r.hidden=!r.textContent.toLowerCase().includes(filter.value.toLowerCase());if(!r.hidden)n++});document.getElementById("asset-count").textContent=n+"項"});</script></body></html>']
(O/'report.html').write_text(''.join(parts),'utf-8')
print('report',len(issues),'findings; source links unresolved:',[(i['id'],s[0]) for i in issues for s in i['sources'] if not s[2]]);print('fragment bytes',V.stat().st_size)



