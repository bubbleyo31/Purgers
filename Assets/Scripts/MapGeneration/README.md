# Assets/Scripts/MapGeneration 程式導覽

可連接地圖區塊的規則。

Phase 4-A 新增純資料 `MapTopologyPlanner`：以穩定 Seed 規劃線性多 Chunk 拓撲，限制回頭方向、拒絕矩形重疊、限制每塊重抽次數，並只在完整成功時指定唯一最後 Chunk。Phase 4-B 已由下層既有 Prototype 在 Host 接入 Level 1～3 Runtime；Planner 本身仍不讀寫 Scene／Prefab。

## 修改入口

- 先由下表依功能找對腳本，再讀取其 `Tooltip`、序列化欄位與公開方法。
- 涉及 Fusion 資料、場景轉場、生成或傷害時，先確認 State Authority／Scene Authority，Client 不可直接寫入結果。
- 新增功能應放進責任最接近的子資料夾；純判定規則保持無場景依賴並搭配 EditMode 測試。

## 直接腳本

| 腳本 | 類別或結構 | 主要責任 |
| --- | --- | --- |
| [MapTopologyPlanner.cs](./MapTopologyPlanner.cs) | `MapTopologyPlanner`、`MapTopologyPlan`、`MapChunkTopologyDefinition` | Phase 4-A 純資料決定性拓撲、固定次數重抽、重疊拒絕與最後 Chunk 身分。 |

Host 場景生成、封口與導航請繼續閱讀下層 `Prototype` README。

## 快速查找

- Inspector 設定：搜尋 `[Header(`、`[Tooltip(` 或 `[SerializeField]`。
- 網路同步：搜尋 `[Networked]`、`[Rpc(`、`HasStateAuthority`、`IsSceneAuthority`。
- 場景綁定：由 `[SerializeField]` 欄位名稱回到 Unity Inspector 找同名引用。

