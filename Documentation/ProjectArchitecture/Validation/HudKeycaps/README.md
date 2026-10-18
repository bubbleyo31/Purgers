# HUD 改鍵提示鍵帽驗證

最後核對日期：2026-10-18（局部驗證）。
正式規則：[167 HUD 鍵帽規則](../../167_已確認HUD美術與整格血量.md#2026-10-18-hud-改鍵提示鍵帽局部核對)、[104 ESC 玩家設定](../../104_ESC選單與玩家設定.md#2026-10-18-改鍵提示呈現同步)。

## 已確認的資產問題與修正

舊版 `BattleHudReferenceLayout.ApplyStage` 將 `grappleKeyBackground` 設為 `null`，且將能量鍵 Image 改成透明底色。已修正作者入口，避免重套時再清空引用。`SafeHouse.unity` 的獨立 HUD 也有同樣的透明背景及空序列化引用。已在 `StageHUD.prefab`、`SafeHouse.unity` 恢復原 `Key.png`、米粉底色與背景引用；HUD 初始化時若引用缺漏，仍會由既有 Label 父物件找回 Image。保留兩個資產其餘現有變更。

`HudKeyPromptLayout` 由現有 `LocalPlayerBattleAbilityHUD` 呼叫，不讀取或裁定按鍵。只在按鍵改變時依共用 `PlayerSettingsData.KeyLabel` 更新文字與鍵帽寬度；使用既有 Image／Sprite，兩側各留 8 單位，背景高 32、最大寬 104，置於上方 UI 底緣下 19 單位。最大寬度內字級由 21 自動降至 10，不換行、不裁字。Q／E 按下與不可用色仍由原 HUD 決定。

## 驗證

- `HudKeyPromptTests` EditMode：3／3 通過。對正式 `StageHUD.prefab` 驗證能量鍵與能力鍵的原圖、Q、Shift、Ctrl、較長鍵名、滑鼠主鍵／側鍵、未配置文字、內距、尺寸、文字不溢位、上方間隔及改回 Q 的尺寸復原。
- `SafeHouse.unity`：由 Unity Editor 開啟檢查並修復能量鍵背景引用與底色；存檔後序列化引用已接回既有 GrappleKey Image，場景其他 HUD 變更均保留。
- [HUD 鍵帽離屏預覽](HudKeycapsPreview.png)：正式 Sprite、TMP 字體與排版 helper 組合呈現 Q、L CTRL、PAGE DOWN、滑鼠側鍵；不依賴 Editor 焦點或遊戲 Play Mode。這是版面檢視，不代表自然遊玩驗收。
- PurgersRegression EditMode：已嘗試啟動，但 Unity 進入 Play Mode 切換且 MCP 工作階段中斷，未取得新鮮 `RunFinished` 結果；不宣稱完整回歸通過。啟動前最近完整回歸的 4 項既有失敗／2 項略過紀錄不等同本輪結果。
- Unity Console 沒有本次 HUD 程式錯誤。可見的 `BattleHudReferenceLayout.cs` `TextureImporter.spritesheet` 過時 API 警告為既有項目，與本次未改的作者工具有關。
- 自然遊玩、按鍵實際重綁後的玩家視覺接受與 Build 尚未驗證。

## 重現離屏預覽

`HudKeyPromptPreview.Render(path)` 在 Unity Preview Scene 使用正式 Prefab Sprite、字體及共用 layout helper 輸出 PNG，不開視窗、不切換焦點。
