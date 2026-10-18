# Assets/Scripts/GameFlow/SafeHouse 程式導覽

Phase 4-E：State Authority 從 Host 存檔發布 StageLevel、CycleLength、CycleStage 與 Boss 身分；HUD 與 Game 共用 `StageHudText`。成功提交返回後顯示下一 Stage，Continue 存檔的 CycleLengthSnapshot 不被目前 Menu 設定覆寫。

`SafeHouseHUD` 以 Runner／StageLevel 向 `StageHudLifetimeRegistry` 接管關卡級 UI；接管時會清除上一個 Game HUD，離開 SafeHouse 或 Flow 失效時也會清空並銷毀自身。

2026-09-22 修正：Fusion Multi-Peer 會搬移 HUD 到 Runner Scene，再卸載原始 Scene；清理回呼必須核對 HUD 的「目前」Scene，不能僅用 Awake 記錄的 handle 銷毀它。安全屋也掛載既有 `LocalMinimapController`，指定 SafeHouse Flow 與 `SafeHouse_Cartography.asset`，NetworkMapState 留空。顯示完整安全屋平面圖與本機玩家；M 展開，範圍 35／55 公尺。投票仍由 SafeHouseFlow 管理，UI 只讀取與傳送意圖。

安全屋灰模修改後執行 `Tools/Purgers/Map/Bake SafeHouse Cartography`。目前工具只支援 `GrayboxEnvironment` 與 `StartTerminal` 的 BoxCollider，使用 `Floor` 頂面及 0.6m 低平台門檻分色；它不是導航可達性驗證，不適用未來多樓層／封閉屋頂／任意 Mesh 地圖。一般 Game 的 NavMesh 烘焙流程不受影響。

安全屋終端機、全員準備確認與關卡載入。

Phase 4 前置-1：終端機與 Ready 請求遵守 AllInput 鎖。切場前重用 GameLogic 的權威生命週期準備鉤子；黑幕與本地輸入鎖由 Runner 回呼統一管理，詳見 [轉場工具](../Transition/README.md)。

## 修改入口

- 先由下表依功能找對腳本，再讀取其 `Tooltip`、序列化欄位與公開方法。
- 涉及 Fusion 資料、場景轉場、生成或傷害時，先確認 State Authority／Scene Authority，Client 不可直接寫入結果。
- 新增功能應放進責任最接近的子資料夾；純判定規則保持無場景依賴並搭配 EditMode 測試。

## 直接腳本

| 腳本 | 類別或結構 | 主要責任 |
| --- | --- | --- |
| [SafeHouseFlowController.cs](./SafeHouseFlowController.cs) | SafeHouseFlowController | 處理 Host 終端機互動、準備確認及轉進關卡。 |
| [SafeHouseHudController.cs](./SafeHouseHudController.cs) | SafeHouseHudController | 安全屋文字提示與準備確認 UI。 |
| [SafeHouseReadyRules.cs](./SafeHouseReadyRules.cs) | SafeHousePhase, SafeHouseReadyRules | 安全屋準備確認的純規則。 |
| [SafeHouseStartTerminalView.cs](./SafeHouseStartTerminalView.cs) | SafeHouseStartTerminalView | 終端機的世界空間提示與可視化。 |

## 快速查找

- Inspector 設定：搜尋 `[Header(`、`[Tooltip(` 或 `[SerializeField]`。
- 網路同步：搜尋 `[Networked]`、`[Rpc(`、`HasStateAuthority`、`IsSceneAuthority`。
- 場景綁定：由 `[SerializeField]` 欄位名稱回到 Unity Inspector 找同名引用。


## 2026-10-10 全員準備快速出發（局部核對）

核對來源：SafeHouseFlowController／SafeHouseReadyRules／SafeHouseHudController 與 SafeHouse 場景。
全員準備即進入 1 秒出發，優先於同 Tick 投票逾時。State Authority 保存同步 PausedReadyCheckTicks、停止投票 Timer；HUD 只顯示出發秒數。取消準備／新加入恢復原投票餘額；ESC 取消整票。場景既有 Countdown Duration Seconds 由 Editor 改為 1，Ready Check Timeout Seconds 保留 7。
設定、手動操作與驗證見 [145 安全屋 Ready Check](../../../../Documentation/ProjectArchitecture/145_PrePhase4-2_安全屋鍵盤準備與HUD.md)。
## 2026-10-13 安全屋限制與 HUD（局部）

安全屋 Q／E 經 PlayerActionGate 封鎖，玩家友傷／自傷經既有 DamageReceiverUtility／PlayerHealth 拒絕；普通武器與 ALT 獎勵維持。SafeHouseHudController.HudRunner 可供技能／經驗子 HUD 綁定；2026-10-13 已依使用者授權接到正式 SafeHouse 場景，保留原安全屋資訊。詳見 Documentation/ProjectArchitecture/145_PrePhase4-2_安全屋鍵盤準備與HUD.md 及 Validation/SafeHouseGameplay/README.md。

## 2026-10-17 左上外觀
使用者指定第一張切角預覽，TopLeftColumn 納入既有 ReferenceFrame，沿用 UtilityPlate／HUD 字型；投票秒數接到 SafeHouseHudController.readyTimerLabel，原準備按鈕保留，新鍵帽是呈現。未接線可沿用舊合併文字。小地圖圖資、35／55 公尺、M 展開 600×600、投票請求與權威時序不變。Inspector 與驗證狀態見 145 文件「安全屋小地圖與投票外觀」。
