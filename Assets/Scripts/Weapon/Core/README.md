# Assets/Scripts/Weapon/Core 程式導覽

此資料夾集中放置相近責任的遊戲程式。

## 修改入口

- 先由下表找對腳本，再讀取其 `Tooltip`、序列化欄位與公開方法。
- 涉及 Fusion 資料、場景轉場、生成或傷害時，先確認 State Authority／Scene Authority，Client 不可直接寫入結果。
- 新增功能應放進責任最接近的子資料夾；純判定規則保持無場景依賴並搭配 EditMode 測試。

## 直接腳本

| 腳本 | 類別或結構 | 主要責任 |
| --- | --- | --- |
| [WeaponDamage.cs](./WeaponDamage.cs) | WeaponHitZoneType | `WeaponDamage` 的主要實作入口。 |
| [WeaponHitUtility.cs](./WeaponHitUtility.cs) | WeaponHitUtility | `WeaponHitUtility` 的主要實作入口。 |
| [WeaponHitZone.cs](./WeaponHitZone.cs) | WeaponHitZone | `WeaponHitZone` 的主要實作入口。 |

## 快速查找

- Inspector 設定：搜尋 `[Header(`、`[Tooltip(` 或 `[SerializeField]`。
- 網路同步：搜尋 `[Networked]`、`[Rpc(`、`HasStateAuthority`、`IsSceneAuthority`。
- 場景綁定：由 `[SerializeField]` 欄位名稱回到 Unity Inspector 找同名引用。


## 命中特效（2026-09-25）

- [PlayerWeaponImpactEffects.cs](PlayerWeaponImpactEffects.cs)：Player Root 的 State Authority → All 命中視覺廣播。
- [WeaponImpactSettings.cs](WeaponImpactSettings.cs)：共享素材、Layer 篩選及容量。
- [WeaponImpactVisuals.cs](WeaponImpactVisuals.cs)：每射手 FIFO 孔、表面跟隨及火花清理。
- [手動配置與驗證](../../../../Documentation/ProjectArchitecture/163_槍械命中特效與彈孔.md)。
