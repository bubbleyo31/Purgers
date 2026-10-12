# Tank 預備衝刺與共用敵人準心

最後核對日期：2026-10-10（局部核對）。
核對來源：TankMeleeCombo／TankMeleeCombo.PreparedDash、TankPreparedDashRules、PlayerMovement、PlayerEnemyAimQuery、LocalPlayerEnemyCrosshair；使用者本次確認。
相關文件：[47 預備與動畫幀](47_Tank預備攻擊與動畫幀時序.md)、[20 移動](20_玩家核心移動與狀態.md)、[40 職業](40_職業與特殊能力.md)、[初版驗證](Validation/TankPreparedDash/README.md)、[寬鬆角與幀期限驗證](Validation/TankDashTiming/README.md)。

## 已確認行為

- atk1／atk2／atk3 只有幀模式已到預備停格後放開，才有衝刺資格。停格前放開仍是原本點按攻擊。
- 放開當下優先沿準心直接命中存活敵人的 Hit Mask；未直接命中時搜尋最大距離及寬鬆半角內的可見敵人，角度最小優先、近似同角度選較近者。候選以 Hitbox 中心（含 Offset）或 Collider bounds 中心檢查角度，再以射線確認未被遮擋且實際命中點仍在角度／距離內；不以場景為目標。地面／空中均適用。
- 有合法目標時，動畫從停格幀繼續播放，衝刺與動畫使用相同期限，在原出傷幀抵達表面前；途中沒有傷害、出手震動或揮擊聲。到出傷時點由原本近戰入口揮擊，揮空仍播放出手回饋。無目標或已在停靠距離內則原地出手。
- 衝刺本身不增加傷害、範圍，不消耗 F 充能，也不啟動 TankAirDash 的 E 技能或其到達 AOE。
- 所有武器共用紅色準心：直接瞄準存活敵人即變紅；移開、遮擋、敵人死亡或玩家失效恢復原色。與鈎索充能、Tank 是否預備、衝刺距離無關。

## 接入及邊界

- `PreparedDashing = 5` 追加至 Tank 原階段列舉，不改既有值。Host 在放開時以 Fusion Lag Compensation 決定是否有合法目標，Client 不可自行授權衝刺或提前出傷。
- 鎖定敵人 NetworkObject 與命中點的局部座標；途中跟隨同一敵人，不因轉動準心重新選敵。保留視角控制，最後傷害方向仍由既有近戰出手判定決定。
- 放開後停止累加兩種按住時間，保留數值供本次攻擊讀取；Combo 仍在完整收招後才開始接續計時。
- 透過既有 PlayerMovement → KCC 最後階段提交速度；膠囊掃掠及 KCC Collision Layer Mask 保留碰撞，最後一步限制速度避免越過終點。停靠以瞄準高度對齊命中位置，不把玩家腳底放在敵人頭部。
- 衝刺期間使用 Action Gate 的 WeaponAction 來源封鎖 Movement／Aim／QuickAction／Grapple；只在衝刺開始取消正在使用的一般鈎索，預備期間仍可正常移動與鈎索。結束只清除自身來源，不清除其他鎖。
- 被障礙擋住或敵人失效時停止位移，動畫繼續，到既定出傷幀才在目前位置揮擊，原近戰距離與 LOS 仍決定是否命中；死亡、換武器、控制封鎖與更高優先位移中斷則清除攻擊，不補出傷。
- `PlayerEnemyAimQuery` 共用直接瞄準及存活敵人分類，Tank 另使用權威寬鬆選敵入口，紅色準心不套用寬鬆角；敵人依現有正式 `TestDamageReceiver` 辨識。Host 查 Fusion Hitbox，本地 UI 查自己的 Runner PhysicsScene Collider，且排除自己。紅色提示不是網路傷害或衝刺授權。
- 現有 LocalPlayerGrappleAimIndicator 保留鈎索提示責任，新的準心顏色元件只修改指定 Graphic 的顏色，停用時恢復人工原色及透明度。

## 手動配置

本次功能以程式與文件交付，未替功能接線 Scene／Prefab，也不替換既有準心圖形。本次使用者明確同意先儲存目前場景再測試，已保存 _Menu 原有修改；這不表示功能已接線。

1. TankProfessionRuntime Prefab 根物件 → Tank Melee Combo。三段先依 [47](47_Tank預備攻擊與動畫幀時序.md) 啟用並配置幀時序。
2. 「預備放開後接近敵人」欄位如下，以下為新程式初值，並非已替使用者調整的資產數值：

| 欄位 | 初值 | 單位／限制與影響 |
|---|---|---|
| Enable Prepared Dash | true | 關閉則回到停格放開原地出手 |
| Prepared Dash Range | 12 | 公尺；正值，直接與寬鬆選敵的最大距離 |
| Prepared Dash Aim Half Angle | 10 | 度；0～90 的半角。0 僅直線；10 代表準心兩側各放寬 10° |
| Prepared Dash Stop Distance | 1 | 公尺；正值，停在敵人表面前，需配合三段既有近戰範圍 |
| Prepared Dash Aim Height | 1.6 | 公尺；KCC 腳底到瞄準起點，需對齊玩家視線高度 |
| 各段幀時序提示「預備衝刺」 | 自動推算 | 秒；由出傷幀減停格幀換算，不需另外填速度／秒數 |
| Prepared Dash Enemy Mask | Hit Mask（第 9 層） | 檢查實際命中子物件 Layer，不看 Enemy Root Layer |
| Prepared Dash Obstruction Mask | DefaultRaycastLayers 排除 UI／Weapon_Visual／ViewModel | 應包含世界牆壁及角色；KCC 位移碰撞另依原 Collision Layer Mask |

3. 在既有準心 `Crosshair/Center` Image 所在物件加入 **Local Player Enemy Crosshair**；Crosshair 可留空取同物件 Graphic，也可指定既有 Image。
4. Enemy Hit Mask 指定 Hit Mask。Obstruction Mask 須包含牆壁，排除 UI 及第一人稱模型。Maximum Distance 初值 100 公尺，Enemy Color 初值紅色，保留原 Graphic alpha。這個距離不表示武器可命中。
5. Aim Camera 指世界相機，留空使用 Camera.main；不要指定 ViewModel 相機。Runner 可留空自動找本地玩家，Multi Peer 測試應指定對應 Runner。
6. 儲存自己的資產配置後重新生成玩家／Runtime；不要把 Play Mode Clone 數值當作已保存。

## 衝刺與動畫共用期限

- 時間 =（出傷幀 − 預備停格幀）÷ 來源 FPS ÷ 播放倍率。例如 6 幀／30 FPS／1 倍速 = 0.2 秒；2 倍速則 0.1 秒。Inspector 自動顯示，不要求人工換算。
- 放開時保存本次持續時間；每個模擬步以剩餘距離 ÷ 剩餘時間求速度，敵人移動時更新終點。既有正值子彈時間移速倍率會被補償以維持動畫期限；倍率為 0 或碰撞阻擋時不可保證抵達，仍以出傷幀及原近戰距離／LOS 判定。
- Fusion 以 Tick 模擬，最短為一個 Tick；零幀間隔也不瞬移穿牆。最後一步限制位移到終點；非整 Tick 的間隔在下一個模擬步觸發傷害，差異最多一個 Tick。
- Input Authority 預測動畫游標與位移，Host 決定選敵、階段完成及傷害。Networked 的 PreparedDashDurationSeconds／PreparedDashElapsedSeconds 隨中斷清零。
- 舊 preparedDashSpeed／preparedDashTimeout 保留序列化但隱藏且不參與行為，避免破壞舊資產資料。現在只調幀配置、角度、距離與停靠距離。
- 無目標或已貼身仍沿用原地立即出手。寬鬆角只影響衝刺選敵，不擴大近戰傷害角度，也不強制轉動相機；原傷害判定仍可能揮空。

## 驗收重點

三段點按不衝刺；停格放開有目標先衝再砍，無目標／隔牆原地揮擊。檢查地面與空中、貼身不倒退、衝刺途中突然出現障礙、敵人移動／死亡、死亡／換裝／控制鎖、Combo 接續與兩種計時。所有武器確認紅色準心、移開／遮擋恢復與原透明度，以及無鈎索充能時仍有敵人顏色提示。Host 與 Client 分別操作；晚加入／重連不可補播先前出手。各項實際證據與限制見驗證紀錄。