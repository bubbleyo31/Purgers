# 開發工具與 AI Agent 工作流

> 最後核對：2026-09-18  
> 核對來源：Unity 2022.3.62f1、`Packages/manifest.json`、`Assets/Scripts`、現有架構與回歸文件  
> 相關文件：[快速讀取](00_CHATGPT快速讀取.md)、[文件維護規則](120_文件維護規則.md)、[回歸驗證](130_回歸驗證與審查狀態.md)

## 1. 本輪選擇

本專案已是 Photon Fusion 第一人稱合作動作遊戲，並已有自訂 FOV、Camera Shake、黑幕轉場、KCC、AI Navigation、測試與 MCP for Unity。工具選擇以「提升驗證能力、不改變 Runtime 行為」為優先。

### 已採用

| 工具 | 版本／鎖定方式 | 用途 |
|---|---|---|
| MCP for Unity | 現有 commit `30d22075093d1d35dfb0091c1c7550e9ad948577` | 讓 Agent 讀寫 Unity Editor、場景、Prefab、Console 與測試；由移動分支改為固定 commit。 |
| ParrelSync | 現有 commit `a122dc90cbe2d4cc2669ddad00b12b3917ef934b` | 獨立 Host／Client 驗證；固定現有 commit，避免無意升級。 |
| Memory Profiler | `1.1.12` | 比較場景切換、重生、多輪遊戲後的 Native／Managed 記憶體與洩漏。 |
| Profile Analyzer | `1.3.4` | 比較多幀 CPU Profile，定位 Fusion Tick、AI 感知、UI 與地圖生成尖峰。 |
| Code Coverage | `1.2.7` | 量測現有 EditMode 回歸實際涵蓋範圍，找出未測的權威規則。 |
| `AGENTS.md` | 專案根目錄 | 讓 Hermes、Codex、Claude Code 等共用相同架構、測試與資產安全規則。 |

以上三個新增 Unity Package 均以 Editor／測試診斷為本專案用途，不要求本輪改寫遊戲 Runtime。Memory Profiler 套件本身包含會自動引用的 Runtime assembly；本輪未呼叫其 Runtime API，也未把它當作遊戲功能相依。

## 2. 本輪刻意不導入

| 候選 | 暫不導入原因 | 重新評估條件 |
|---|---|---|
| DOTween | 黑幕、FOV、Camera Shake 已有專案專用生命週期與所有權；現在加入只會形成第二套流程。 | 新 UI 動效量明顯增加，且先限定為本地 Presentation 層。 |
| UniTask | 目前 async 使用集中於少數 Menu 連線流程，沒有足以合理化全專案 Runtime 相依的遷移目標。 | 大量可取消的載入／網路工作出現，並能先為取消與場景切換寫測試。 |
| Cinemachine | 第一人稱 World／Weapon Camera、FOV 疊加與 Shake 已有專案專用架構。 | 要新增觀戰、過場或第三人稱鏡頭，且可清楚隔離現有第一人稱 Camera Rig。 |
| Addressables | 尚未看到自有程式以 Addressables 管理內容；只安裝而不制定分組、釋放與更新策略會增加複雜度。 | 關卡／裝備內容量或遠端更新需求確立，先設計記憶體與版本策略。 |
| Odin Inspector | 付費授權，且本輪主要痛點是多人／效能驗證，不是 Inspector 工具建置。 | 有合法授權，且重複 Custom Editor 工作可量化。 |
| DI Framework | 現有仲裁層與 Prefab 組裝已成形；全域導入會造成高遷移風險。 | 新系統可先在獨立邊界驗證，不搬動 Fusion NetworkBehaviour 生命週期。 |

## 3. Agent 執行順序

```text
讀 AGENTS.md
→ 讀 00 快速文件
→ 讀任務對應文件
→ 核對實際 C#／Prefab／Scene
→ 檢查 Git 工作樹，保留既有未完成變更
→ 測試先行建立垂直切片
→ 最小實作
→ 等待 Unity 編譯並讀 Console
→ focused test
→ PurgersRegression（total 必須 > 0）
→ 需要時執行 Host／Client Play Mode
→ 依 120 更新文件
→ 獨立 diff 審查
```

## 4. 建議量測基線

### Memory Profiler

至少比較：

1. `_Menu` 初始狀態。
2. `_Menu → SafeHouse → Game → SafeHouse` 一輪後。
3. 重複三輪後。
4. Host 加入與 Client 離線後。

重點觀察 `NetworkObject`、Texture、Mesh、AudioClip、Managed Shell、事件訂閱與場景保留物件是否持續成長。Snapshot 可能包含敏感內容，不提交 `MemoryCaptures`；`.gitignore` 已排除該目錄。

### Profile Analyzer

在 Development Player 的相同硬體與相同關卡條件，分別擷取：

- 無敵人基線。
- 典型戰鬥數量。
- AI 高密度壓力場景。
- Chunk 生成與 NavMesh 接合時段。

不要只用 Editor Profile 判斷玩家端效能；正式結論以目標平台 Player 為準。

### Code Coverage

先記錄現有 `PurgersRegression` 的覆蓋基線，不設定武斷的全專案百分比門檻。優先補強：

- State Authority 規則與拒絕路徑。
- 玩家控制鎖票證合併。
- 場景轉換／載入屏障。
- 存檔損壞、回滾與 Host-only 寫入。
- 能力生命週期、死亡／重生清理。

MonoBehaviour、Fusion weaving、序列化與多人時序仍需 Play Mode／Host-Client 驗證，Coverage 不能取代它們。

套件預設會在專案根目錄產生 `CodeCoverage` 報告與歷史資料；該目錄已加入 `.gitignore`。CI 應以 `-coverageResultsPath`／`-coverageHistoryPath` 明確輸出至建置產物目錄，不提交產生檔。

## 5. 套件升級規則

1. 不直接把 Git dependency 指向 `main`、`master` 或其他移動分支。
2. 一次只升一個高影響套件。
3. 升級前保存目前 commit／版本與測試結果。
4. 升級後至少驗證 Unity 編譯、非零測試數、MCP 連線，以及 ParrelSync Host／Client 啟動。
5. 套件升級若改變 Runtime、Prefab 或工作流程，必須在本文件與回歸文件記錄實際結果。

## 變更紀錄

- 2026-09-18：依現有多人架構選用三個低侵入診斷套件，固定 MCP for Unity 與 ParrelSync 現有 commit，建立跨 Agent 專案規則；未改寫任何 Runtime 遊戲系統。
