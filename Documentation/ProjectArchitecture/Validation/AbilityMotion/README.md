# 技能位移地面／空中一致性（V4）

日期：2026-10-08。使用者核准「後座力新增衰減時間，先量測閃現再決定修正」，且保留空中施放。配置見 [45 手冊](../../45_E技能後座力配置.md)。沿用 PlayerMovement／PlayerSlideController／KCC，不新增移動控制器，不保存 Scene／Prefab／Definition。

## 原因與最終變更

後座力原直接疊加 DynamicVelocity，由 EnvironmentProcessor 使用地面／空中不同摩擦。現改為每技能 Runtime 的有限時間位移，初速線性降至零，以每 Tick 的區間積分累加 ExternalDelta，仍經 KCC CCD／碰撞。Movement Speed／Direction／Timing 值保留，新增 Movement Decay Duration 初始 0.2 秒；舊已啟用資產手感會改變，需要手動試調，不能宣稱與舊地面行程完全相同。

閃現原已在摩擦之後覆寫 KinematicVelocity 並限制剩餘距離，基準與本輪量測地面／空中均為 8 m、結束後殘速 0，碰牆約 3.05 m 停止。未重現使用者所述閃現差異，因此沒有重寫 PlayerBlinkAbility，也沒有新增使用限制。實際關卡地形、帶方向輸入、斜坡／邊緣等自然條件仍需重現，不能宣稱所有閃現情境已修復。

後座力被牆壁擋掉的部分消耗、不累積；被高優先移動接管的 Tick 也不補發。普通鉤索附著、外部拉玩家、Tank／Blink 衝刺仍優先。空中緩速縮放額外位移但不保存進原速度返還；子彈時間沿用縮放。向上後座力在原 Processor 抑制當次 GroundSnap，碰撞保持。

## 自動化證據

- [expected-failure.json](expected-failure.json)：先新增 5 項純規則測試，Job bf6cbc0bf7384c648867c06f744e237b，5 項因缺少 AbilityMovementRules 按預期失敗。
- [baseline.txt](baseline.txt)：真實 Single Runner 基準。初速 10、觀察約 0.8 秒：地面後座力 0.3450928 m，空中 3.914185 m；閃現兩者均 8 m、殘速 0。最早量測工具曾將舊 ActivationSequence 當新施放，已先修正等待新序號，再取得這份有效基準。
- [focused.json](focused.json)：Job 3a4bd390b6614da69673da09c0ec22dd，47/47 通過。包含衰減 30／60／120 Hz、末段、重疊向量、原後座力／鏡頭回正與 ActiveControlRules。
- [regression.json](regression.json)：最終 Job 28cb493674c745c388f33ae315696143，探索 582、完成 567，回報既有 BossStageAssetTests.GameSceneContainsBossAssetWiring 引用失敗，result=null；不推算其他 566 項通過。
- [single-runner-motion.txt](single-runner-motion.txt)：24 項 PASS。10 m/s、0.4 s 的水平後座力地面 1.999878 m、空中 2 m，途中落地約 1.999756 m；帶原側向速度仍有相同後座力距離且原速度保留。碰牆約 1.05 m 停止、移除牆壁不補發；向上峰值約 0.70 m（重力仍作用）；閃現地／空 8 m、碰牆停止。
- [single-runner-recoil.txt](single-runner-recoil.txt)：56 項 PASS。保留鏡頭平滑／回正／槍械交互與 V3 閃現兩階段；移動事件斷言更新為正確排入剩餘距離，而非舊 DynamicVelocity。

- [controlled-host-client.txt](controlled-host-client.txt)：30 項 PASS，同 Editor 雙 Runner，包含真實 Client E、Host 權威、Proxy 限權、晚加入／重連、閃現兩階段。新增水平位移在確認 pitch=0 且地面 grounded=True／空中 grounded=False 後量測：地面 1.999878 m、空中 2 m，Input Authority 與 Host 結果收斂。先前工具的地面起點曾讀到瞬移前殘留接地旗標；最終已改為 Fixed Tick 擺位、先等待 KCC 更新，再確認實際接地，不將先前無效量測當成通過證據。
- 最終 [final-checks.json](final-checks.json)：compileFailed=false、compiling=false、Console error/warning=0、已退出 Play Mode、_Menu.unity dirty=false；24 份技能／職業 Runtime Prefab 與本輪基準 SHA256 比較 0 差異。

## 邊界與重跑

這些為受控 Single Runner／同程序 Host-Client，沒有獨立雙程序／雙機、Build 或人工手感驗收。單人位移 probe 是正常 Player 輸入與真實 KCC；不是只呼叫數學函式。測試只用臨時物件與 Runtime 實例設定，finally 關閉 Runner，最後退出 Play Mode。特殊移動仲裁有靜態接線與既有回歸覆蓋，未宣稱所有自然組合均實測。

1. EditMode 聚焦 AbilityMovementRulesTests、AbilityRecoilTests、ActiveControlRulesTests、RecoilRecoveryTests、WeaponRecoilApplicationTests。
2. EditMode 全分類 PurgersRegression；以完成數及失敗明細判讀，不以探索數取代實跑數。
3. Play Mode 無其他 Runner 時，AbilityMotionRuntimeVerification.Begin()；讀取 Status／Stage 與 Temp/AbilityMotionRuntimeVerification.txt。
4. 前項結束後可跑 AbilityRecoilRuntimeVerification.Begin()，再跑 AbilityRecoilNetworkVerification.Begin()；後者需要 Photon 連線，使用不公開的隨機測試房間。查看各自 Temp 文字結果，最後退出 Play Mode。
