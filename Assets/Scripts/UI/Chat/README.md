# Assets/Scripts/UI/Chat 程式導覽

此資料夾集中放置相近責任的遊戲程式。

## 修改入口

- 先由下表找對腳本，再讀取其 `Tooltip`、序列化欄位與公開方法。
- 涉及 Fusion 資料、場景轉場、生成或傷害時，先確認 State Authority／Scene Authority，Client 不可直接寫入結果。
- 新增功能應放進責任最接近的子資料夾；純判定規則保持無場景依賴並搭配 EditMode 測試。

## 直接腳本

| 腳本 | 類別或結構 | 主要責任 |
| --- | --- | --- |
| [LocalChatPanel.cs](./LocalChatPanel.cs) | LocalChatPanel | `LocalChatPanel` 的主要實作入口。 |
| [NetworkChatSystem.cs](./NetworkChatSystem.cs) | NetworkChatSystem, LocalChatMessageKind, LocalChatMessage | `NetworkChatSystem` 的主要實作入口。 |

場景切換時，持續存在的 Primary GameLogic 會接收新場景替身的出生設定，再 Despawn 替身。替身上的 NetworkChatSystem 會先檢查 `GameLogic.IsPrimaryForRunner` 並略過 Spawned，避免把正常的 SafeHouse／Game 設定交接誤報成同場景重複聊天系統；同一 Primary 上真的出現第二個聊天系統時仍會輸出錯誤。

## 快速查找

- Inspector 設定：搜尋 `[Header(`、`[Tooltip(` 或 `[SerializeField]`。
- 網路同步：搜尋 `[Networked]`、`[Rpc(`、`HasStateAuthority`、`IsSceneAuthority`。
- 場景綁定：由 `[SerializeField]` 欄位名稱回到 Unity Inspector 找同名引用。

