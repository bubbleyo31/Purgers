# 血量 HUD 離線隱藏修正

最後核對日期：2026-10-17（局部）。

## 變更

透過 Unity Editor 的 SerializedObject 修改並保存 `_Menu.unity`：LocalPlayerHealthSlider.healthVisualRoot 由 HealthBar 改為其父 Health_UI。Health_UI 包含 HealthBar、既有文字及使用 HealthCross.png 的 MomentumArrow；控制器 PlayerHUDController 位於此範圍之外，不會因隱藏而停止 Update。保留子物件原本啟用狀態，沒有新增斷線事件或改動權威資料。

與操作前備份比較，最終場景差異只有這一個引用。Editor 保存時順帶補入音效 maximumWorldLoopVoices=32（原始碼同值預設），已精確移除該次自動補寫以保留原有序列化狀態；正式引用修改由 Editor 完成。

## 已取得驗證

- 在場景控制器的暫時副本呼叫原有 SetVisualVisible(true)／UnbindCurrentHealth：修改前 barHidden=true、crossHidden=false；修改後兩者皆 true，controllerEnabled=true。副本隨即銷毀。
- EditMode 聚焦：HealthWholeCellTests、MenuSessionEndTests、NetworkChatDepartureTests 共 16/16 通過，job `7f968bffd92d4c2087d6e393d72140ef`；之後 Console error=0。
- 已啟動完整 PurgersRegression，job `25441042f1e64d4c99e0c08243baeab1`；期間 Domain Reload 後 MCP 失去 Unity session，重試仍無法取得結果。本輪不宣稱完整回歸通過。
- 本輪未完成新的 Play Mode、獨立 Host／Client 或重新入場實測；先前 RoomClosure 記錄不代表本次資產修正後已重跑。

## 人工驗收

進入遊戲確認血條與紅色十字正常；Client 自行離開、Host 關房導致 Client 離開時，Health_UI 整組應隱藏。返回主畫面後再次加入，玩家完成綁定時應恢復原本血量介面。不可停用外層 PlayerHUDController。