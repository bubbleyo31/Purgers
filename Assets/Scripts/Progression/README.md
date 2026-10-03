# Assets/Scripts/Progression 程式導覽

玩家成長與持久化資料。

## 修改入口

- 先由下表依功能找對腳本，再讀取其 `Tooltip`、序列化欄位與公開方法。
- 涉及 Fusion 資料、場景轉場、生成或傷害時，先確認 State Authority／Scene Authority，Client 不可直接寫入結果。
- 新增功能應放進責任最接近的子資料夾；純判定規則保持無場景依賴並搭配 EditMode 測試。

## 直接腳本

| 檔案 | 職責 |
|---|---|
| `PlayerExperienceRules.cs` | Phase 6-A 的純資料規則：全隊共享經驗、擊殺者 +1、升級需求、超額經驗、待選獎勵期間停止收經驗。尚未與擊殺／Fusion Runtime 接線。 |
| `Save/PlayerExperienceSaveBridge.cs` | Phase 6-B 將 Host 的同步經驗資料還原／寫回 `host` 存檔記錄；訪客進度不使用 Host 身分寫檔。 |

存檔與 Runner Context 請繼續閱讀 `Save/README.md`。

## 快速查找

- Inspector 設定：搜尋 `[Header(`、`[Tooltip(` 或 `[SerializeField]`。
- 網路同步：搜尋 `[Networked]`、`[Rpc(`、`HasStateAuthority`、`IsSceneAuthority`。
- 場景綁定：由 `[SerializeField]` 欄位名稱回到 Unity Inspector 找同名引用。

