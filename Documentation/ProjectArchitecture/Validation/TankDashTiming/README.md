# Tank 寬鬆選敵與動畫期限衝刺驗證

日期：2026-10-10。正式規則：[48](../../48_Tank預備衝刺與共用敵人準心.md)。

## 範圍與證據

- 本次延續既有 Tank 預備衝刺，只改選敵寬鬆角與動畫／衝刺共用時間；紅色準心仍採直接命中。
- Red：9 個新測試全數因尚無相應方法而按預期失敗；job 41c1af89174047dc959a43d9ce2b8673。
- 第一輪聚焦 EditMode：21/21 通過，job 6dd43c931a854a88977914e16a13a4c7。包含新 9 項與既有預備／衝刺規則；完整聚焦與全回歸紀錄待下列補充。
- 受控 Single Runner：[single.txt](single.txt)，25 項檢查通過。真實 Player／Tank Runtime／NetInput／Fusion Runner／KCC／Enemy Health；測試分身使用固定 SphereCollider，不改正式資產。
- 驗證動畫游標於衝刺中推進但不提前扣血、幀間隔 0.3 秒期限、出傷時已達停靠距離、原近戰傷害入口、側方目標選敵與移動、0° 關閉寬鬆角、點按／牆壁／中斷與 Combo。
- 純規則測試另驗證近／遠距離、移動目標的剩餘距離重算、倍率換算、最後一步不超越及正值移速倍率補償；不把這些算式測試視為正式敵人移動姿勢或子彈時間場景驗收。
- 已依使用者本次明確授權儲存 _Menu 原有 dirty 修改再測試；未為本功能新增或修改 Scene／Prefab 接線。Unity MCP TestRunner 會自動保存 dirty named scenes，依據套件 TestRunnerService.SaveDirtyScenesIfNeeded。
- 正式模型姿勢、空中、途中目標死亡、實際死亡／換裝、多人延遲手感、獨立執行檔與正式 HUD 畫面仍需人工驗收；不以受控分身或一般 Reset 代替。
- 重跑：空閒 Play Mode 執行 TankPreparedDashRuntimeVerification.Begin(false/true)，輸出 Temp/TankDashTiming。只建立可拋棄的 Runner 與 Clone。

- 同 Editor 受控 Host／Client：28 項檢查通過，見 [host-client.txt](host-client.txt)。Client 送出真實 NetInput，Host 決定衝刺與扣血；確認 Client 收到衝刺階段、動畫推進、側向衝刺與重連後無重播。這不是獨立執行檔或 WAN 延遲驗收。
- 完整聚焦 EditMode：38/38 通過，job a1f57860ef4b4d0381d4f108225daa3c，含新增 9 項與既有 Tank 預備、行為、動畫資產和衝刺測試。
- 完整 PurgersRegression：job 5a45e5dc3e9148a0a425bd86b75ca07b，completed=623、discovered total=638、result=null。工具仍列既有 BossStageAssetTests.GameSceneContainsBossAssetWiring 引用不一致失敗（同初版衝刺驗證），不宣稱全回歸通過，不推算其餘通過數。
- 最終 Editor：isCompiling=false、scriptCompilationFailed=false、isPlaying=false、_Menu dirty=false。保留使用者原場景；編譯期間未使用舊速度／逾時欄位的 CS0414 已以局部 pragma 標示僅供舊序列化保留，其他既有警告未改動。
- 本輪程式空白檢查及正式規則／索引／驗證文件相對連結檢查通過；新測試 .meta 已由 Unity 產生。Console 工具最終 error 查詢回傳 0 項；Editor LogEntries 可讀清單亦無錯誤項，未把 TestRunner 歷史計數當成編譯結果。
