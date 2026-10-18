# 鈎索尖刺驗證紀錄

日期：2026-10-14。Unity 2022.3.62f1／Photon Fusion。
範圍：新 GrappleHit 接觸技能 `grapple.spike`；[定案與 Inspector](../../33_鈎索尖刺技能.md)。

## 實作／資產

- 新 Runtime、規則、命中間隔表、純本機尖刺視覺、Definition 與範例 Loadout 已建立。
- Unity Editor 資產工具建立序列化資產及 meta，未手改 Unity YAML。
- 初次技能實作未替玩家裝備、未加入獎勵池、未修改正式 Scene。後續使用者已確認 V2 圖示並授權入池，接線與驗證見 [RewardIcon 紀錄](RewardIcon/README.md)。測試前後 Assets/Scenes 下現有場景 SHA256 相同；保留原有未提交工作。
- 以專案設定的本機 Unity MCP HTTP 端點連線；沒有在其他 Unity 專案執行。
- 新視覺 1 Mesh／1 Material／0 貼圖，160 頂點、192 三角形。預覽為佔位外觀，不是使用者美術驗收。

## 實際結果

| 層級 | 結果 | 證據 |
|---|---|---|
| 前置失敗 | 16 個規則案例確實因 GrappleSpikeRules 不存在失敗 | red.json（MCP 沒回 summary，但保存 completed=16 與逐例失敗） |
| 聚焦 EditMode | 21／21 通過 | focused.json；全回歸 XML 也包含相同 21 項全部 Passed |
| 完整 PurgersRegression | 816 Passed、1 Failed、1 Skipped，共 818 | final-tests.xml、final-tests-summary.txt |
| 隔離 Single Runner | 25 項 PASS | single.txt |
| 同 Editor 受控 Host／Client | 30 項 PASS | host-client.txt |
| Unity 編譯 | 完成，未見新增技能編譯錯誤；測試腳本 Assert 命名衝突已修正後重編譯 | editor-final.json、實際 Editor 測試／Runner 執行 |
| Console | 不是零訊息；最終錯誤稽核只留下隔離 fixture 的 Director 缺失與既有職業 Driver 找不到 AttackFocus 訊息 | console-error-audit.json |

唯一全回歸失敗為既有 `BossStageAssetTests.GameSceneContainsBossAssetWiring`（Game Scene 的 Boss Prefab 引用比較）。本次未修理／覆寫該 Scene。
略過的是 Explicit `AbilityToggleInputTests.HostClientInputLateJoinAndReconnectUseAuthoritativeToggleState`，不是尖刺測試。

## Runtime 涵蓋

- 透過正式 AbilityRuntimeManager 裝備技能；Idle／Shooting 不生效；Attached 無敵人時不亮、不開始期限。
- 兩個敵人各有兩個 Collider，仍各只收一次 1.5 倍傷害；同 Tick 不重傷，0.5 秒之後按新量條取得 2 倍傷害，不消耗量條。
- 首次命中才啟動；斷繩隱藏而期限不變；預設斷繩慣性不可用，打開選項才可用；重新 Attached 不刷新。
- 前後位置掃掠可命中中間敵人；世界牆阻擋。
- 沒有鈎索時效果照樣到期；冷卻擋傷；冷卻結束、保持 Attached 但無目標時只待命。
- 本機視覺存在、位於 Owner 所屬 Runner Scene，中心與半徑一致。
- 下一輪出傷使用既有 EnemyStateController 微擊退，向外水平位移，沒有暈眩或追加傷害；死亡停止視覺資格。
- 遠端 Input Authority 不能提交傷害；同步固定期限；斷線再以新 Client 加入的觀察端可重綁 Owner 並接收現有期限，不重啟效果。

## 驗證方法與限制

測試器只改臨時 instance，凍結測試玩家的自動模擬，再直接呼叫正式 SimulateAbility；高速個案直接驅動同一 ScanContacts 掃掠入口。這是 State Authority 受控驗證，不是自然 Q 按鍵／KCC 飛行手感驗收。
Host／Client 是同一 Editor 內的獨立 Runner 與 PhysicsScene；沒有獨立程序、Windows Build、網路延遲／丟包或效能量測。重新加入檢查的是既存技能的晚加入觀察端，不等於原 PlayerRef 重連後重新取得控制權。
未專項驗證子彈時間 Deferred、所有敵人免疫組合、正式 Reward 選取流程、最終第一／第三人稱視覺；這些沿用既有接口，但不可宣稱已人工驗收。

## 過程中修正的驗證問題

1. 新腳本尚未匯入時首次工具回 total=0；未算通過，顯式匯入並确认程序集可見後重跑。
2. 完整回歸的 MCP job 卡在舊 progress=34，實際 Unity 已結束。加入由 TestRunner ICallbacks.RunFinished 直接保存的 XML；以上 818 項以 XML 為準，舊 job 不作成功證據。
3. 直接移動網路敵人不能穩定建立空範圍測試；改用 Host Despawn 清空 fixture，再驗證待命。
4. 臨時 AddComponent 建立的 NetworkEvents 未初始化 AOI UnityEvent，晚加入曾有 NullReferenceException；補齊 fixture 的 UnityEvent 後重跑 Host／Client，最終錯誤稽核已無該例外。
5. Single 最終 25 項在上述 AOI fixture 修補前、相同 Gameplay／視覺程式通過；Host／Client 30 項在修補後通過。全回歸在最後兩個 fixture 修正前、相同 Gameplay 程式通過尖刺 21 項；後續只有測試 fixture、文件及分類註解檔尾空行變更。

## 重跑與手動驗收

1. Editor 執行 `Tools → Player Ability → 建立鈎索尖刺資產（僅補缺）`。
2. EditMode：GrappleSpikeRulesTests + GrappleSpikeIntegrationTests，再跑 PurgersRegression。可先呼叫 GrappleSpikeValidationResults.Register() 保存 XML。
3. 沒有現存 Runner 的 Play Mode：GrappleSpikeRuntimeVerification.Begin(false) 或 Begin(true)。讀 Status／Stage；輸出在 Temp/GrappleSpike。
4. 正式手動測試：將 GrappleSpike Definition 放入 Loadout；確認自然鈎索、跨敵人、斷繩／再鈎索、0.5 秒間隔與 3+3 秒循環。再測開關 Release Momentum、多人視覺及貼牆衝撞。
5. 最後驗收基礎傷害 20、半徑 1.5、微擊退距離 0.6／速度 6 是否合適；皆為暫定 Inspector 值。

![尖刺佔位視覺預覽](spike-preview.png)

