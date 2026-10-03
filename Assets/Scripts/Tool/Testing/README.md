# Assets/Scripts/Tool/Testing 程式導覽

`Phase4ENetworkVerification` 只在 Editor／Development Build 編譯，使用 `--phase4e-role <Host|Client> <session> <outputDirectory> <delaySeconds> <stageLevel> <cycleLength> <static|cycle>` 明確啟動。`cycle` 模式驗證成功提交、SafeHouse 返回與重新進場；`static` 模式驗證指定普通關。它會以反射縮短成功提交與切場等待，只是自動化探針，不是正式 Gameplay 入口。

`Phase4DNetworkVerification` 只在 Editor／Development Build 編譯，使用 `--phase4d-role <Host|Client> <session> <outputDirectory> <delaySeconds>` 明確啟動。標準驗證以兩個獨立 Windows Player 執行：Host 建立 Level 3，Client 延遲加入並比對 Host 快照；同時檢查完整拓撲、封口、Collider／Cartography、最後 Chunk、撤離選擇及 Host-only NavMesh／Patrol 權威。

`Phase44NetworkVerification` 同樣只在 Editor／Development Build 編譯，必須明確啟動或傳入 `--phase44-client <session> <outputDirectory>`。使用隔離存檔、暫時移動兩位玩家，驗證配置／封口一致、個人探索、共享切換、展開與撤離標記；不在一般遊戲啟動，不改正式存檔。

`Phase41NetworkVerification` 僅編譯在 Editor／Development Build，且必須明確呼叫 StartVerification 或傳入 `--phase41-client <session> <outputDirectory>` 才執行。它使用獨立存檔資料夾，延後 Client 載入回報 10 秒，驗證 Host 等待、返回安全屋重進及等待時 Client 斷線。它會在測試 Session 移動玩家到終端機／撤離點；不可當成正式 Gameplay。詳見架構文件 144。

此資料夾集中放置相近責任的遊戲程式。

`Phase42NetworkVerification` 延用 `--phase42-client <session> <outputDirectory>` 與獨立存檔執行 Ready／取消／逾時／倒數驗證。Phase 4 前置-3 更新為檢查三列容器、純數字關卡時間及本機中下方動能增傷 UI，並保存 Ready／Stage 截圖。直接啟動 Runner 的測試入口會隱藏主選單；不呼叫依賴正式 MenuConnection 的 Gameplay Menu Show。正式連線流程不受影響。

## 修改入口

- 先由下表找對腳本，再讀取其 `Tooltip`、序列化欄位與公開方法。
- 涉及 Fusion 資料、場景轉場、生成或傷害時，先確認 State Authority／Scene Authority，Client 不可直接寫入結果。
- 新增功能應放進責任最接近的子資料夾；純判定規則保持無場景依賴並搭配 EditMode 測試。

## 直接腳本

| 腳本 | 類別或結構 | 主要責任 |
| --- | --- | --- |
| [CombatRegressionProbe.cs](./CombatRegressionProbe.cs) | CombatRegressionProbe | `CombatRegressionProbe` 的主要實作入口。 |
| [Phase4ENetworkVerification.cs](./Phase4ENetworkVerification.cs) | Phase4ENetworkVerification | Phase 4-E CycleLength、Stage 循環、HUD 與 Host／Client 雙程序探針。 |

## 快速查找

- Inspector 設定：搜尋 `[Header(`、`[Tooltip(` 或 `[SerializeField]`。
- 網路同步：搜尋 `[Networked]`、`[Rpc(`、`HasStateAuthority`、`IsSceneAuthority`。
- 場景綁定：由 `[SerializeField]` 欄位名稱回到 Unity Inspector 找同名引用。


此驗證工具複製 Photon AppSettings，將兩端 FixedRegion 固定為 asia，避免 Editor／Player 各自快取不同最佳區域而找不到房間；不修改原設定資產或正式 MenuConnection。

2026-10-02：GrappleEnergyNetworkVerification 只在明確 --grapple-energy-test 參數下建立 Development 雙程序探針，使用獨立存檔。受控扣款不代表自然出鈎物理驗收；見 [鈎索架構](../../../../Documentation/ProjectArchitecture/30_鈎索系統.md)。
