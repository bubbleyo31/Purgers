# 後座力 V3：準備與成功施放分離

日期：2026-10-08。範圍：閃現二次 E、共用成功施放契約、失敗不發 AfterCast；閃現移動後座力依使用者確認略過，保留衝刺清除 DynamicVelocity 的原行為。完整設定見 [45](../../../45_E技能後座力配置.md)。

## 證據

- 先修改受控 Fixed Tick 測試，再改正式程式：AfterCast 單選的閃現第一次 E 不得踢鏡頭。修正前確實在此失敗，見 [expected-failure.txt](expected-failure.txt)。
- 聚焦 EditMode 23/23 通過，Job d1ed63fed42c44d4bbdc402b10046111；見 [focused.json](focused.json)。這些為既有設定、方向、Flags、槍械施加與回正规則測試；新增施放情境由下方 Runtime 驗證。
- 受控 Single Runner 55 項 PASS，見 [single-runner.txt](single-runner.txt)。包含 Blink Timing 1／2／3 分別配置、第一次準備／第二次成功衝刺、長按與 Movement 封鎖、待命逾時、投射物在前搖後失去 Prefab 導致施放失敗時兩通道均不追加；原有施加／回正／槍械交互仍通過。
- 完整 PurgersRegression 探索 577／完成 562，Job be301001931846fc9e768cf55d11cd0b，回報同一項既有 BossStageAssetTests.GameSceneContainsBossAssetWiring 引用失敗，result=null。不宣稱全回歸通過，也不推算其餘 561 項通過；見 [regression.json](regression.json)。

## 契約與驗證邊界

Activate() 與 OnArmedPress(NetInput) 改為 protected bool；原方法名、序列化類別／欄位及 Timing 0／1／2／3 保持。一般技能完成前搖時，效果成功建立才回傳 true；閃現第二次 E 完成所有限制並進入 Dashing 才回傳 true。取樣準星／方向先於後座力。空中緩速、空中衝刺保留 InstantActivation 合併一次。

未增加動畫等待或修改冷卻、移動仲裁。沒有保存 Scene／Prefab／Definition。未執行獨立雙程序／雙機、Build 或人工手感；支持技能的成功入口探測不代表全部天然觸發條件通過。對一般發射失敗採用前搖後 Prefab 缺失的受控案例，不宣稱全部場景障礙組合已覆蓋。

## 重跑

沿用 [V2 重跑步驟](../V2/README.md#重跑)：先 EditMode 聚焦與全回歸；無其他 Runner 的 Play Mode 呼叫 AbilityRecoilRuntimeVerification.Begin()，完成後再呼叫 AbilityRecoilNetworkVerification.Begin()。後者需 Photon 網路連線。各讀取 Status／Stage 及 Temp 下對應文字檔，最後退出 Play Mode。

- 同程序 Host／Client 22 項 PASS，見 [host-client.txt](host-client.txt)。新增真實 Client 第一次 E 只待命、長按不衝刺、第二次 E 一次鏡頭後座力及 Input Authority 同步；保留 Shield、Proxy、晚加入與重連測試。
- 最終 compileFailed=False、Console error/warning=0、Play Mode=False、場景 dirty=False。工作區已有技能人工配置（榴彈、貫穿砲、精準鎖敵、衝擊扇波）；本輪沒有保存／還原這些資產。V2 舊雜湊已不代表目前配置，不以其差異宣稱本輪修改資產。
