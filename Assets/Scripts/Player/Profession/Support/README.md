# Assets/Scripts/Player/Profession/Support 程式導覽

此資料夾集中放置相近責任的遊戲程式。

## 修改入口

- 先由下表找對腳本，再讀取其 `Tooltip`、序列化欄位與公開方法。
- 涉及 Fusion 資料、場景轉場、生成或傷害時，先確認 State Authority／Scene Authority，Client 不可直接寫入結果。
- 新增功能應放進責任最接近的子資料夾；純判定規則保持無場景依賴並搭配 EditMode 測試。

## 直接腳本

| 腳本 | 類別或結構 | 主要責任 |
| --- | --- | --- |
| [SupportAerialAbility.cs](./SupportAerialAbility.cs) | SupportAerialAbility | `SupportAerialAbility` 的主要實作入口。 |
| [SupportGrapplePullAbility.cs](./SupportGrapplePullAbility.cs) | SupportGrapplePullAbility | `SupportGrapplePullAbility` 的主要實作入口。 |
| [SupportProfessionRuntimeDriver.cs](./SupportProfessionRuntimeDriver.cs) | SupportProfessionRuntimeDriver | `SupportProfessionRuntimeDriver` 的主要實作入口。 |
| [SupportRifleHealingAbility.cs](./SupportRifleHealingAbility.cs) | SupportRifleHealingAbility | `SupportRifleHealingAbility` 的主要實作入口。 |

## 快速查找

- Inspector 設定：搜尋 `[Header(`、`[Tooltip(` 或 `[SerializeField]`。
- 網路同步：搜尋 `[Networked]`、`[Rpc(`、`HasStateAuthority`、`IsSceneAuthority`。
- 場景綁定：由 `[SerializeField]` 欄位名稱回到 Unity Inspector 找同名引用。

