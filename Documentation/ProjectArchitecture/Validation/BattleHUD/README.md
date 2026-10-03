# 戰鬥 HUD PDF 排版驗證

日期：2026-09-24（PDF 校正後）。來源：[UI.pdf](../../UI設計參考圖/UI.pdf)，Canva 畫布 1920×1080，PDF 頁面 1440×810 pt。舊 1166×656 PNG 只保留歷史，不再是驗收基準。規格：[154](../../154_戰鬥HUD視覺配置第一版.md)。

## 可查看的畫面

- [常態，1440×810，與 PDF 頁面同尺寸](normal-1440x810.png)
- [獎勵選窗，1440×810](reward-1440x810.png)
- [1280×720](normal-1280x720.png)
- [1920×1080](normal-1920x1080.png)
- [2560×1080](normal-2560x1080.png)：整組維持原圖比例置中。

這些圖由 `BattleHudReferenceLayout.RenderPreview` 載入實際 `_Menu` 玩家 HUD 與 `StageHUD.prefab` 的 Graphic／TMP 渲染而成。白底方便和原圖比較，圖中生命／充能／經驗設為滿值，彈數為 16/25，技能槽用六角星示例；說明與圖示使用代表資料。它們驗證幾何、間距、圓角、層級與縮放，不是多人遊玩截圖。

相較原圖，關卡框保留實際關卡／目標文字，倒數使用有效的 08:36，獎勵框加入必要說明。武器剪影採使用者提供素材，具有與概念圖略異的槍身輪廓。中文字型使用實際 Noto Sans TC Bold；彈數／容量皆使用 MuseoModerno-Bold，其他文字統一 NotoSansTC-Bold。圖中技能為已配置能力的佔位圖示，獎勵說明為代表資料；使用者最新指定取消 ALT 白色遮罩；目前不再淡化背景，故非 PDF 像素逐點複製。

## PDF 校正階段測試（歷史）

- PDF 校正先確認舊 Prefab 仍是 1166×656 而失敗：job `fb7b27e9dfb14410a1a022435da46a63`，非 0 項。
- `BattleHudReferenceLayoutTests`、`BattleHudV1Tests`、`HudLayoutTests`、`Phase6RewardHudPrefabTests`：46/46 通過（job `55949134e6bf421c80b47a430a09d6aa`，其後完整測試亦包含此組）。測試包含不同解析度保持等比、Frame 不超出父視窗、現有 UI 引用與資料／按鍵契約。
- 完整 EditMode：199 項執行，192 通過，7 失敗；最終 job `51d6c754ba31413f8445207540af25a7`。失敗與排版修正前相同：兩項 Boss 資產、四項經驗規則／開發升級、獎勵清單一項。曾有一次初始化逾時、實際 0 項，已排除並重試，沒有算成通過。
- `_Menu` 與 `StageHUD` 缺失腳本均為 0；Game Scene instance 已繼承 ReferenceFrame、小地圖引用有效，實例的 `CanPresentRewardSelection` 為 true。
- 預覽工具開發時曾出現 RenderTexture 仍被 Camera 引用的釋放警告；已修正 finally 清除 targetTexture 的順序。

## 執行階段邊界

仍須實戰檢查滑鼠移動／ALT 選窗、技能冷卻、血量碎片、換彈進度與超長文字。Host／Client、晚加入與場景往返不是這組靜態圖的證據，不宣稱已驗收。

後續美術修改以 Scene／Prefab 與 154 座標表為準；`ApplyStage`／`ApplyPlayer` 是重設版面的作者工具，不能當每次遊戲啟動程序，也不可用來覆蓋後續手動微調。

## 可重建來源

`UI-PDF-geometry.json` 保存曲線每段 24 次取樣的輪廓、精確包圍盒與來源頁／物件。隨附 Python 擷取腳本重建 JSON 的 SHA256 完全一致。Unity Sprite 以 2 倍設計尺寸並做覆蓋率取樣；圓環以 PDF 內外徑建立。原始 PDF 與使用者圖片保留原始位元組。

[歷史像素區域對照數據](pdf-geometry-comparison.json) 記錄本輪字型／抗鋸齒修正前的 PDF 第 1 頁與 Unity 預覽的藍色輪廓遮罩重合度；關卡文字、抗鋸齒與細環邊緣會影響門檻結果，此數據不代表逐像素完全一致。最終 Console 錯誤／警告 0，Scene／Prefab Missing Script 皆為 0。

## 部分血格／抗鋸齒／字型／遮罩修正

- [95 血量近看](health-detail-1200x280-hp95.png)、[90 血量近看](health-detail-1200x280-hp90.png)：每格 20；最後一格保留斜率，右側以裁切呈現剩餘量。
- [95 血量完整 1080p](normal-1920x1080-hp95.png)、[取消遮罩的獎勵窗](reward-1920x1080.png)。白底是預覽相機底色，不是獎勵遮罩。
- 上述圖的 RenderTexture MSAA=1（關閉），邊緣來自 Graphic 的螢幕像素透明漸層。近看圖重新渲染為 2 倍比例，並非將小圖放大。
- 新增 4 項回歸全部通過，job `f663c33684814cd58f3630cb0302e23a`：半格與四分之三格保留斜率／左頂點、透明抗鋸齒外緣、Prefab 關閉遮罩與 Noto Bold。第一次測試中 mesh 反射選到重載歧義，已改為 DeclaredOnly 後驗證。
- 本輪完整 EditMode 共執行 203 項，只有先前相同的 7 項失敗，沒有新增失敗；job `f14768f537d549e890ada9cda6edfd58`。前一次 job `3b80a97a1f474357991cb185433a8471` 初始化逾時、0 項，未計為通過。
- 僅套用字型、遮罩與血格邊緣設定，沒有重跑完整排版。未新增多人權威或傷害邏輯；實戰受傷／治療／ALT 往返仍需遊玩驗收。

## 清理舊物件後的武器與換彈修復

- 已重現：刪掉舊單一數值 TMP 後，驗證／Update 仍將其視為必填，雙 TMP 更新也會解引用空值；無 Sprite 的 Filled Image 在 0%、25%、80% 時網格寬度都為 100%。失敗 job `4d1cc9f17dc54d08a6d38870f36faecd`，4 項均命中問題。
- 修正後同組 4／4 通過，job `ecab59b49c184d4ea3e2d556775d07ff`。涵蓋雙 TMP／Icon、彈數不變仍更新進度、完成／取消及來源清除後隱藏。
- [換彈開始](normal-1920x1080-reload0.png)、[50% 完成](normal-1920x1080-reload50.png)、[90% 完成](normal-1920x1080-reload90.png) 使用實際 HUD 元件套用 Snapshot 渲染，沒有手動改量條比例；不是 Host／Client 換彈錄影。
- 原有物件刪除保留；修復沒有重建舊 TMP。實戰需確認兩職業正常換彈、取消、切換與無限彈匣狀態。

- 容量分隔格式亦先重現失敗（job `4769360710db4b6ca32d15cf674bf587`）：空字串／帶空白斜線都曾輸出 ` / 25`，已修為單行 `/25`。此組因此增為 5 項。
- 首次完整回歸 job `58e1ff8d04914d7298f8496fa0909ad7` 在初始化逾時，0 項，未計為通過。

- 最終 `PurgersRegression` 類別執行 **193 項**（發現總數 208，不等同本輪執行數）；原先 7 項 Boss／經驗／獎勵清單失敗不變，本次 5 項武器 HUD 測試均通過。job `95f836ec11954e3c81e8845599b70921`。另一次完整啟動 `5993955a286b45f7849a8caf8853af90` 亦為 0 項初始化逾時，排除成功計數。
- 最終必要 UI 引用缺失 0、舊 `weaponValueText` 為 null、Fill 為 Simple、Scene 已儲存、Console 新增 Error／Warning 0。

## 技能槽置中／選獎勵滑鼠隔離（2026-09-24）

- 修正前 6 項聚焦測試均重現問題，job `f1d53ac558e54a72a4f1a7ce9f4db826`：三種長寬比圖示偏離槽中心約 5.2／9.6／5.7 設計像素，三個滑鼠鍵缺少放開才解除的狀態。
- 修正後聚焦 9／9 通過，job `1614c00f96244e23a8fedb39a4b299fd`，包含四項 Catalog、預設排除後兩候選、中卡從隱藏恢復、滑鼠消耗與實際 Sprite mesh。
- 完整 `PurgersRegression` 執行 200 項（發現 215 項），195 通過、5 項既有失敗；job `58e8f93a4de244f0b30ade9d05e927c8`。失敗：BossStageAssetTests.GameSceneContainsBossAssetWiring、DevelopmentLevelUpRulesTests.GrantsExactlyTheMissingExperienceForNextLevel、PlayerExperienceRulesTests.CrossingThresholdKeepsOverflowAndQueuesReward／OneLargeGrantCanQueueSeveralRewards／RequirementDoublesFromTen。
- 原要求五項 Catalog 的舊測試已依使用者確認改驗四項；獎勵資產與抽選規則未改。另一個原有 Boss 必備元件失敗本輪未重現，不歸功於本次 UI 修正。
- `RepairAbilitySlotCentering` 只更新既有模板 Icon／Fallback 中心與舊偏移欄位；沒有重跑整體排版。常駐 [1920×1080 預覽](normal-1920x1080.png) 改為呈現目前預設 Loadout 的拉近／緩降圖示，並非強制使用同一星形。
- Editor 編譯完成、Console Error／Warning 0、StageHUD Missing Script 0。此次未執行實戰／獨立 Host-Client 驗證。

### 實戰驗收順序

1. 使用預設 Loadout 進入關卡，觀察 Q／E 圖示中心；替換成不同長寬比的圖示，槽位間距及中心仍相同。
2. F8 取得待選升級，按住 ALT：目前四項池排除兩件裝備後，僅左右卡；中鍵不領獎。
3. 按住左鍵選取後繼續按住：不得射擊；先放 ALT 仍不得射擊；放左鍵再按才射擊。右鍵同樣不得提前 ADS。測試先按住射擊／瞄準再開 ALT，也須中斷並等放開再按。
4. Client 重複上述操作，確認選擇由 Host 確認且只消耗一次獎勵；中卡僅在真實候選數為三時顯示，過期選單請求不扣獎勵。

## 2026-09-24 使用者確認與本輪交接

- 使用者回報「目前沒問題了」，本輪技能圖示置中、獎勵按鍵隔離與候選顯示問題已獲使用者確認；維持四項獎勵池，候選不足時只呈現左右兩項。
- 使用者另有手動微調顏色與位置。後續以目前 Scene／Prefab 實際設定及 instance overrides 為基準，先檢查差異再局部修改；本次未重新量測每個微調值，也未替使用者儲存或套用 Editor 中可能尚未儲存的設定。
- `UI.pdf`、154 的原始座標表與本資料夾既有渲染圖保留為歷史設計依據，可能早於上述微調。不得以重跑 `ApplyStage`／`ApplyPlayer`、重建 Prefab 或還原舊截圖的方式覆蓋使用者調整。
- 本次只更新紀錄，未修改程式、Scene、Prefab、素材或重新執行 Unity 測試。上一輪聚焦 9／9、完整回歸 200 項中 5 項既有失敗，以及獨立 Host／Client 尚未驗證的證據邊界均保留。使用者本次確認未提供測試環境，不能擴張為多人測試已通過。
- 後續 UI 設計更新回到本對話延續；閱讀順序：[00 快速讀取](../../00_CHATGPT快速讀取.md) → [154 UI 規格](../../154_戰鬥HUD視覺配置第一版.md) → [159 獎勵 HUD／輸入](../../159_Phase6-E_ALT獎勵HUD與輸入.md) → 本紀錄 → 實際 Scene／Prefab 與程式。

## 2026-09-24 後續手動驗收回報

使用者進一步回報 Phase 6 目前測試通過，並一併確認 Phase 4 也已測試通過。這更新了上節「未提供測試環境」時的整體手動驗收狀態；本輪仍只更新紀錄，未重新執行 Unity 測試，也未收到逐項 Host／Client 日誌。既有完整回歸 200 項中 5 項失敗等測試數字不變。統一狀態與證據層級見 [130 回歸驗證與審查狀態](../../130_回歸驗證與審查狀態.md)。
