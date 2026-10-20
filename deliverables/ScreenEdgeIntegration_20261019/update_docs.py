from pathlib import Path
import json
changes=[]
def save(p,text): changes.append({'path':str(p),'content':text})
root=Path(__file__).resolve().parents[2]/'Documentation/ProjectArchitecture'
link='[112 玩家畫面外框](112_玩家畫面外框狀態效果.md)'
heading='## 2026-10-19 治療、受傷與橘金外框正式接入（局部核對）'
entries={
'01_專案架構總覽_給開發者.md':f'PlayerHealth 的實際回血／扣血事件與 PlayerGrappleMomentumEnergy.NormalizedEnergy → LocalPlayerScreenEdgeEffects → ScreenEdgeGraphic／共用 Shader → HUD 下方透明外框。只綁有效本機 Input Authority，沒有新增玩法狀態、RPC 或傷害／動能 Owner。正式 _Menu 已接線，詳細設定與驗證邊界見 {link}。',
'02_程式職責總表.md':f'''| 類別／檔案 | 單一責任 |
|---|---|
| LocalPlayerScreenEdgeEffects | 綁定有效本機玩家事件與動能快照、清除與三狀態優先權；不決定回血、傷害或增益 |
| ScreenEdgeEffectsSettings／ScreenEdgeStyle | 共用 ScriptableObject 樣式、時間、混合參數及正式 Shader 引用 |
| ScreenEdgePulse | 單次／持續事件刷新包絡與有限權重 |
| ScreenEdgeGraphic | 不攔點擊的四片 UGUI 外框網格及私有執行期材質 |
| ScreenEdgeEffects.shader／ScreenEdgeEffectsCore.cginc | Runtime UGUI 合成與 Editor／Runtime 共用程序外觀 |
| ScreenEdgeEffectsSetup | Editor 冪等 Prefab／_Menu 配置；保留既有序列化元件 |
| ScreenEdgeEffectsVerification／ScreenEdgeEffectsTests | 真實 HUD 離屏渲染與聚焦資產／包絡測試 |
| ScreenEdgeRuntimeVerification | Editor 限定、手動啟動的受控 Host／Client 驗證與清理 |

PlayerAbilityColorGrading 僅在同一玩家的新治療外框有效接管時讓出治療 LGG；其他技能調色不變。設定、責任與驗證見 {link}。''',
'100_UI觀戰與聊天.md':f'已在 _Menu/GameplayHUD Canvas/PlayerHUDCanvas 新增 ScreenEdgeEffects Prefab，Canvas Order=-5，介於護目鏡 -10 與原 HUD 0／StageHUD 100；保持護目鏡既有 sibling 0，外框附加於末尾，顯示層級由 Canvas 控制。全畫面 Stretch 位於 ReferenceFrame 外，不攔 Raycast、不改 HUD 版面。只呈現本機生命／動能，不跟觀戰對象。詳見 {link}。',
'43_E技能視覺與UI配置.md':f'PlayerAbilityColorGrading 新增 LocalPlayerScreenEdgeEffects.OwnsHealing(observedHealth) 的讓出條件：只有正式外框綁定同一玩家且 Shader／治療強度有效時，原治療 LGG 權重歸零，避免全畫面重複染綠；外框缺少／停用／治療強度零則保留舊 LGG。其他技能與保命調色仍由原系統管理。詳見 {link}。',
'49_玩家受傷來源指示.md':f'新增的紅色畫面外框訂閱相同 LocalDamageReceived，但僅 AppliedDamage>0 觸發；完全格擋／全吸收不觸發。既有方向弧形、來源與格擋提示不變。沒有以 HP 差值或觀戰資料猜傷害。單次及連續事件接續包絡見 {link}。',
'31_鈎索動能與傷害倍率.md':f'橘金外框只讀 PlayerGrappleMomentumEnergy.NormalizedEnergy，與既有動能量條同源；其漸進權重與退去只是本機呈現，不修改 KCC 速度、倍率、保持時間或權威狀態。正式外框設定與驗證見 {link}。',
}
for name,body in entries.items():
    p=root/name;s=p.read_text(encoding='utf-8-sig')
    if heading not in s:save(p,s.rstrip()+'\n\n'+heading+'\n\n'+body+'\n')
p=root/'00_CHATGPT快速讀取.md';s=p.read_text(encoding='utf-8-sig')
banner=f'> **2026-10-19 畫面外框正式接入（局部核對）**：治療／受傷沿 PlayerHealth 實際結果事件；橘金增益沿既有動能比例。單次與連續事件共用刷新包絡，只作用於本機，Canvas -5 放在 HUD 下方。Prefab／_Menu 已接線；設定、驗證與待驗邊界見 {link}。\n\n'
if banner not in s:save(p,banner+s)
for name in ['HealingEdgePreview','DamageEdgePreview','MomentumEdgePreview']:
    p=root/'Validation'/name/'README.md';s=p.read_text(encoding='utf-8-sig')
    banner='> **歷史預覽範圍**：本頁的「尚未實裝」描述適用於當時預覽版本。2026-10-19 使用者已另行授權正式接入；現況請讀 [112 正式模組](../../112_玩家畫面外框狀態效果.md) 與 [正式驗證](../ScreenEdgeEffects/README.md)。原預覽成果保留供對照。\n\n'
    if banner not in s:save(p,banner+s)
Path(__file__).with_name('doc-changes.json').write_text(json.dumps(changes,ensure_ascii=True),encoding='ascii')
print('Updated module routes and historical preview scope; keyword index untouched.')
