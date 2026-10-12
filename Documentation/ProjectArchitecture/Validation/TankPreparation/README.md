# Tank 預備攻擊驗證

日期：2026-10-10。正式規則與手動設定見 [47](../../47_Tank預備攻擊與動畫幀時序.md)。

## 已執行

- 先加入測試，確認紅燈：`d68725267a9a4daab0f0cd9e26c9dc4f` 實際 3 項失敗，缺少 AnimationFrameTiming、預備計時與幀欄位。更早一次 0 tests 的回傳已判為未執行，不算測試成功。
- 最終聚焦 EditMode：`597005c07626427aa542f358fe022634`，**20／20 通過**。涵蓋幀倍率換算、點按、無限預備、放開跳出傷、重新按下不可重新預備、緩衝計時、非法幀／倍率、舊秒數保留，以及預設 State 路徑與真實 Tank Controller 一致。
- 完整 `PurgersRegression`：`abefcf59de8b4e779befc736d38c737c`，progress 顯示 **605 項完成／620 項探索**，失敗清單一項 `BossStageAssetTests.GameSceneContainsBossAssetWiring`，比較 MapChunk_Boss 物件引用失敗。回傳 result=null，**不能宣稱完整回歸全過，也不能把探索數當實際通過數**。本輪沒有修改該 Scene／Boss 資產或測試；先前 [武器獎勵驗證](../WeaponRewards/README.md) 已記錄同一失敗。
- 受控 Single Runner：**25 項檢查通過**，結果見 [single.txt](single.txt)。使用真實 NetInput 經 Player → Runtime 驅動，而非直接跳階段。實際 Enemy Health 證明長按不傷害、放開才出傷，LocalAttackPerformed 證明預備無基礎震動、出手／揮空有一次。
- 同 Editor Host／Client：**32 項檢查通過**，見 [host-client.txt](host-client.txt)。Host 自己操作、預備中 Client 晚加入、完成後 Client 斷線重連，再由 Client NetInput 操作自己的 Tank，Host／Client 的 SwingSequence 一次前進且權威仍屬 Host。
- Unity 編譯確認 `EditorUtility.scriptCompilationFailed=false`；無本次程式編譯錯誤。既有 DemoController.camera 隱藏警告、BattleHudReferenceLayout 使用過時 spritesheet API，以及測試建立 Player 的 Required Components 診斷不在本次修復範圍。

## 實測修正與證據邊界

測試曾暴露預設 State 路徑不符：現有 Controller 是 `Tank_Base Layer`，已修正程式預設並加入資產契約測試。Unity 曾保留新增欄位的舊匯入快取；確認 ViewModel Prefab 非 dirty 後，對該 Prefab 執行 ForceUpdate 匯入，沒有儲存或改寫 Prefab。最終實測讀到 speed=0、normalizedTime=0.2、正確 attack1 State；完成後恢復 speed>0。

測試腳本初版在換回 Tank 後未送出新的左鍵邊緣，死亡測試未開始攻擊；修正為先送出放開 Tick 再按下，最後死亡清理通過。測試腳本也改為讀元件實際綁定的子 Animator，而非根物件另一個 Animator。額外緩衝測試先確認正式 Prefab 的 Buffer 原本關閉，再只於測試 Clone 啟用後驗證；沒有更動正式配置。

受控測試只調整 Runtime Clone：測試幀為 1 秒／30 FPS／1 倍、預備 6、出傷 15、收招 21；未寫回正式 Prefab。這組值僅驗證行為，不是使用者核准的動畫幀位。

同 Editor 雙 Runner 使用既有 `ShockwaveVerificationSceneManager` 的隔離 Physics Scene 測試工具；不載入正式多人關卡，不等於兩個獨立程序、自然玩家操作或正式場景切換驗收。最後兩輪皆包含預備中移動與收招期間提前按住的真實輸入緩衝檢查，長按等待超過正式 1.4 秒 Combo 接續期限。

## 尚待驗收

- 使用者在三段填入自己的幀位置並啟用模式後，確認實際手部姿勢、銜接與視覺手感。
- 獨立 Host／Client 執行檔與真實網路條件下的畫面、震動及延遲手感。
- 其他玩家技能／敵人的幀模式整合、預備門檻特殊能力：均不在本次實作範圍。

## 重跑

1. EditMode group_names 使用 `TankPreparation` 與 `TankAnimationAssetTests`，完整回歸使用 category_names=`PurgersRegression`；確認非零數量。
2. 空閒 `_Menu` Scene 進 Play Mode，呼叫 `TankPreparationRuntimeVerification.Begin()`；讀 `Status`／`Stage`，報告在 `Temp/TankPreparation/single.txt`。
3. 同 Editor 雙 Runner 用 `Begin(true)`，報告在 `Temp/TankPreparation/host-client.txt`，須等待結束及 Runner 清理完成。
4. 離開 Play Mode，不儲存 Scene／Prefab。Runtime 測試對照值不得覆蓋使用者正式設定。

最終 Editor 狀態：非 Play Mode、Scene 非 dirty、scriptCompilationFailed=false。三段正式資產幀模式皆維持未啟用、Buffer=false、Combo 接續期限 1.4 秒；需使用者自行配置。程式／文件 scoped diff 與 Markdown 連結檢查通過。
