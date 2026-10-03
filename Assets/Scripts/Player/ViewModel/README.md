# Assets/Scripts/Player/ViewModel 程式導覽

玩家移動、職業、能力與第一人稱表現。

## 修改入口

- 先由下表依功能找對腳本，再讀取其 `Tooltip`、序列化欄位與公開方法。
- 涉及 Fusion 資料、場景轉場、生成或傷害時，先確認 State Authority／Scene Authority，Client 不可直接寫入結果。
- 新增功能應放進責任最接近的子資料夾；純判定規則保持無場景依賴並搭配 EditMode 測試。

## 直接腳本

| 腳本 | 類別或結構 | 主要責任 |
| --- | --- | --- |
| [FirstPersonMuzzleFlash.cs](./FirstPersonMuzzleFlash.cs) | FirstPersonMuzzleFlash | 只讀成功射擊序號的本地槍口 Billboard、單一物件重播及清理。 |
| [FirstPersonMuzzleFlashPlayback.cs](./FirstPersonMuzzleFlashPlayback.cs) | FirstPersonMuzzleFlashPlayback | 前進序號去重、回捲防重播與有限時間播放。 |
| [FirstPersonTankViewModelAnimator.cs](./FirstPersonTankViewModelAnimator.cs) | FirstPersonTankViewModelAnimator | `FirstPersonTankViewModelAnimator` 的主要實作入口。 |
| [FirstPersonViewModelActionAnimator.cs](./FirstPersonViewModelActionAnimator.cs) | FirstPersonViewModelActionAnimator | `FirstPersonViewModelActionAnimator` 的主要實作入口。 |
| [FirstPersonViewModelAimAnimator.cs](./FirstPersonViewModelAimAnimator.cs) | FirstPersonViewModelAimAnimator | `FirstPersonViewModelAimAnimator` 的主要實作入口。 |
| [FirstPersonViewModelMotionController.cs](./FirstPersonViewModelMotionController.cs) | FirstPersonViewModelMotionController | `FirstPersonViewModelMotionController` 的主要實作入口。 |
| [FirstPersonViewModelMotionProfile.cs](./FirstPersonViewModelMotionProfile.cs) | FirstPersonViewModelMotionProfile | `FirstPersonViewModelMotionProfile` 的主要實作入口。 |
| [ProfessionViewModelManager.cs](./ProfessionViewModelManager.cs) | ProfessionViewModelManager | `ProfessionViewModelManager` 的主要實作入口。 |
| [WeaponViewModelReferences.cs](./WeaponViewModelReferences.cs) | WeaponViewModelReferences | `WeaponViewModelReferences` 的主要實作入口。 |

## 快速查找

- Inspector 設定：搜尋 `[Header(`、`[Tooltip(` 或 `[SerializeField]`。
- 網路同步：搜尋 `[Networked]`、`[Rpc(`、`HasStateAuthority`、`IsSceneAuthority`。
- 場景綁定：由 `[SerializeField]` 欄位名稱回到 Unity Inspector 找同名引用。

