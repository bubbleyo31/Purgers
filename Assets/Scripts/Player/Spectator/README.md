# Assets/Scripts/Player/Spectator 程式導覽

玩家移動、職業、能力與第一人稱表現。

## 修改入口

- 先由下表依功能找對腳本，再讀取其 `Tooltip`、序列化欄位與公開方法。
- 涉及 Fusion 資料、場景轉場、生成或傷害時，先確認 State Authority／Scene Authority，Client 不可直接寫入結果。
- 新增功能應放進責任最接近的子資料夾；純判定規則保持無場景依賴並搭配 EditMode 測試。

## 直接腳本

| 腳本 | 類別或結構 | 主要責任 |
| --- | --- | --- |
| [LocalPlayerSpectatorController.cs](./LocalPlayerSpectatorController.cs) | LocalPlayerSpectatorController | `LocalPlayerSpectatorController` 的主要實作入口。 |

## 快速查找

- Inspector 設定：搜尋 `[Header(`、`[Tooltip(` 或 `[SerializeField]`。
- 網路同步：搜尋 `[Networked]`、`[Rpc(`、`HasStateAuthority`、`IsSceneAuthority`。
- 場景綁定：由 `[SerializeField]` 欄位名稱回到 Unity Inspector 找同名引用。



## 第一人稱觀戰（2026-10-14）

沿用 LocalPlayerSpectatorController 的延遲／目標生命週期，Presentation partial 負責只讀來源路由、眼睛姿態、模型與效果綁定。CameraFollow.SetWorldPose 的 owner 只作清除識別，不追隨觀戰控制器 Transform。

設定及完整手動驗收見 [100_UI觀戰與聊天](../../../../Documentation/ProjectArchitecture/100_UI觀戰與聊天.md)。不從本元件決定死亡、復活、救援或玩家控制權。


## 2026-10-16 倒地救援接點

倒地保留 PlayerHealth／原物件且仍須撤離；真正死亡才銜接原觀戰。安全屋外不排程自動復活，安全屋沿用原流程。正式規則、Inspector、HUD 人工接線及驗證入口見 `Documentation/ProjectArchitecture/62_倒地與隊友救援.md`。

## 2026-10-18 切換與身分列

Selection partial 提供同 Runner 健康／倒地候選與固定 Q／E；LocalSpectatorHUD 只讀同一名單，姓名沿用 GameLogic，頭像經 PlayerPortraitProvider。全滅轉場由 StageFlow／LocalSceneTransition 協調，規則與驗證見架構文件 100、144 及 Validation/SpectatorSwitch。
