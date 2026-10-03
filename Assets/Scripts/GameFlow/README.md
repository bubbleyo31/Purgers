# Assets/Scripts/GameFlow 程式導覽

存檔循環、安全屋與關卡轉場。

Phase 4 前置-1 新增：[Control 共用控制鎖](Control/README.md)、[Transition 黑幕與載入](Transition/README.md)。關卡計時現在等待所有在線玩家對當次 StageFlow 的載入回報。

## 修改入口

- 先由下表依功能找對腳本，再讀取其 `Tooltip`、序列化欄位與公開方法。
- 涉及 Fusion 資料、場景轉場、生成或傷害時，先確認 State Authority／Scene Authority，Client 不可直接寫入結果。
- 新增功能應放進責任最接近的子資料夾；純判定規則保持無場景依賴並搭配 EditMode 測試。

## 直接腳本

這是分類節點，請繼續閱讀下層資料夾的 README。

## 快速查找

- Inspector 設定：搜尋 `[Header(`、`[Tooltip(` 或 `[SerializeField]`。
- 網路同步：搜尋 `[Networked]`、`[Rpc(`、`HasStateAuthority`、`IsSceneAuthority`。
- 場景綁定：由 `[SerializeField]` 欄位名稱回到 Unity Inspector 找同名引用。

