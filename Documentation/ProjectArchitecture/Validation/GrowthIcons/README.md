# 成長圖示與鈎索能量圖示套用

日期：2026-10-17。這是目前確認的 15 張成長獎勵（6 被動＋9 屬性）與四爪鈎索基本圖示的整合紀錄。

## 已套用資產

- `Assets/_Project_Assets/UI/GrowthIcons/V5/`：16 張確認版 PNG；B01 使用使用者最後附件，SHA256 為 `B575F0BE994E31CF39F2EA0752D07F410C6649EB5012F31AACC3E716CBC3281D`。
- `RankBackgrounds/`：9 張去除中央 I 的底圖；由 ImageGen 參考確認圖只移除數字，逐張看過結果。來源、提示與輸出見 [生成紀錄](RankBackgroundGeneration.json)，檔案雜湊見 [清單](AssetManifest.json)。
- 原圖保留 1254×1254，Unity 匯入為 Sprite Single／Full Rect、最高 256、Alpha Is Transparency、無 Mipmap、無壓縮。25 張 RGBA32 像素資料約 6.25 MiB，不含 Unity 物件與字型。
- 15 份 Reward_*.asset 已接上圖示；九種屬性另接無字底圖與字框。槍械高手保留 JohnWick enum、StableRewardId 與原效果數值。
- StageHUD.prefab 與 SafeHouse.unity 的三張獎勵卡已配置 GrowthRewardRankView／GrowthRank 文字；兩處能量環均新增 64×64 GrappleBodyIcon。
- `_Menu.unity` 與套用前備份 SHA256 相同。SafeHouse 原有其他未提交內容保留；本次與備份比較只增加圖示物件／元件與接線。
- 套用前備份在 `Temp/GrowthIconsBackup_20261017/`；此為本機暫存備份，不應當成版本庫長期備份。

## 階級與呈現

獎勵卡顯示「本次領取後的下一階」，沿用既有 GameLogic 成長讀取與 next 計算，圖示、標題、說明共用此值。換彈速度／近戰攻速／彈匣容量 I～IV，其餘 I～V，共 42 個有效階級。被動沒有階級文字。沒有更動成長規則、網路資料、存檔、能力／武器傷害或能量消耗。

文字使用既有 NotoSansTC Bold SDF，按大寫字形尺寸排版。成長圖示完整縮入六角框，右上 +／−／>> 不被 Cover 裁掉；切回其他獎勵恢復既有 Cover。原本 Growth Icon 仍保留 I 級圖，供未使用 RankView 的既有入口相容。

## Unity 渲染檢查

- [42 個階級與六種被動](AllRanks.png)：由正式 StageHUD 卡片複製後隔離渲染，逐一檢查字串及 TMP 字元數。圖表卡片為 86×100 檢視尺寸；正式卡片底板約 71.29×82.95，未改正式版面。
- [鈎索能量空／半滿／全滿](GrappleEnergy.png)：正式 HUD 子樹的隔離 Canvas 渲染，中央圖示固定，原環形填量分別為 0／0.5／1。測試圖刻意不啟用缺能量提示。
- 這些是 Unity 實際 UI 渲染，沒有進行自然遊戲流程、獨立 Host／Client、Build 或效能量測；不把渲染與接線等同實戰驗收。

## 自然操作核對

1. 從既有流程進安全屋與關卡，確認能量圈中央都有四爪圖，使用鈎索時只有填環依原規則減少。
2. 開啟 ALT 獎勵窗，確認九種屬性的圖示與標題使用同一個下一階；尚未取得為 I，已取得第一階的同類候選為 II，最高依原上限為 IV 或 V。
3. 切換被動、普通技能與武器候選，確認沒有殘留羅馬數字；成長圖示 +／−／>> 完整。
4. 確認槍械高手顯示交叉雙槍，六種被動與九種屬性效果沿原規則執行。

## 測試與引用核對結果

- 實作前聚焦測試按預期失敗：MaximumHealth 缺少圖示、缺少階級呈現類別（job `d83d76845c2247e38776071ec18a060e`）。
- 實作後聚焦 EditMode：10／10 通過，含三張卡實際換階／退出、完整 Sprite UV、文字座標映射與既有填框行為。[工具結果](FocusedResult.json)。
- 最終 PurgersRegression：**981 項，978 通過、2 失敗、1 略過**。以跨 Domain Reload 的 RunFinished 回呼保存 [完整結果](FullRegression.txt)，本次 GrowthIconIntegrationTests 3／3 與 AbilityIconFrameFitTests 7／7 皆通過。
- 兩項失敗均為已記錄的範圍外問題：`BossStageAssetTests.GameSceneContainsBossAssetWiring` 的 Boss Chunk 引用不符；`ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt` 於反射 Field 設定發生 NullReference（測試第 80 行）。本次沒有修改這兩個系統，未宣稱全回歸綠燈。
- Unity 編譯完成；最後 Console error 查詢為 0。回歸過程仍曾出現既有 Player Required Components、Undo 及換彈鏡頭測試訊息，不把歷史／測試警告算成本次新增警告。
- [接線核對](Bindings.txt)：6 張卡、2 個能量圈引用正常；全部 34 份已實作獎勵皆有 HudIcon；25 張貼圖均不超過 256，RGBA32 像素資料總計 6,553,600 bytes。
- 此次套用後未做自然遊戲視覺驗收或獨立多人驗證；完整回歸中的既有 Play Mode 測試不等於本批圖示的實戰驗收。

## 2026-10-17 後續：已確認正式尺寸修正

使用者確認正式尺寸及彈匣數字左右等距後，已套用作者倍率、穩定字級及九張屬性底圖取樣設定。上方無 Mipmap／全部約 6.25 MiB 為初次套用狀態；目前九張 RankBackgrounds 啟用 Mipmap、Trilinear、Bias -0.5，25 張貼圖全載入的像素資料約 7 MiB。原始 PNG、卡片座標與 Scene／Prefab 配置保留。最新正式尺寸渲染、測試與限制見 [ApprovedSize](ApprovedSize/README.md)。
