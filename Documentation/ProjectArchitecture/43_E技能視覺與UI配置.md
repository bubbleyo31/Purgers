# E 技能視覺與 UI 配置手冊

最後核對日期：2026-10-04（局部原始碼與序列化欄位核對）  
核對來源：主工作區 `M:/UnityProject/Purgers` 的 `Assets/Scripts/Player/Ability/Active/`、`Assets/Scripts/UI/Player/`、`Assets/Scripts/Editor/ActiveAbilityIconSetup.cs`、`ActiveAbilityHudSetup.cs`、`PlayerActiveAbilityVisualSetup.cs`；`ActiveAbilityColorGrading.asset`、`StageHUD.prefab`、`_Menu.unity` 與三種技能投射物 Prefab。  
相關文件：[42 技能玩法與設定](42_E主動技能規格與強化預留.md)、[100 UI 架構](100_UI觀戰與聊天.md)、[154 HUD 參考排版](154_戰鬥HUD視覺配置第一版.md)、[60 傷害與生命](60_傷害生命死亡與回饋.md)、[130 驗證狀態](130_回歸驗證與審查狀態.md)。

本文件記錄目前程式接口、配置與待執行驗收。下面的檢查項目是操作要求及預期結果，**不代表本文件已完成編譯、測試、畫面或多人驗證**。實際執行證據另記於 [ActiveAbilities 驗證目錄](Validation/ActiveAbilities/README.md)。

## 1. 責任與資料流

| 顯示內容 | 資料與呈現 Owner | 不應修改的玩法來源 |
|---|---|---|
| 圖示／技能名稱 | `PlayerAbilityDefinition.HudIcon`／`DisplayName`，既有技能槽與獎勵卡共用 | Loadout、獎勵資格及裝備互斥 |
| 中央技能時間條 | `IPlayerAbilityHudState.TimedHudState` → `LocalPlayerBattleAbilityHUD` | State Authority 的階段、`PhaseTimer` 與 `PhaseDurationSeconds` |
| 淡金護盾整格 | `LocalPlayerHealthSlider` 讀本機 `PlayerShieldAbility.CurrentShield` → 同一 `LocalHealthSegmentView` | 正式護盾吸收與 `PlayerHealth` |
| 畫面四色 | `PlayerLocalView` → `PlayerAbilityColorGrading.BindLocal`；只處理有效 Input Authority 玩家 | 技能效果、傷害、經驗與治療結果 |
| 投射物模型、轉向、漂浮 | `PlayerAbilityProjectile.Render` → `PlayerAbilityProjectilePresentation` 的純外觀子物件 | 投射物 Root、同步速度、掃掠碰撞及拾取 |
| 爆炸、貫穿砲、扇波 VFX | 已確認的同步呈現資料 → `PlayerAbilityLocalVfxSlot` | 正式命中範圍、傷害及擊退 |

UI 與調色只讀當前本機玩家；死亡、重生及解除綁定時必須清掉前一個角色的顯示。世界技能模型／VFX 則由各端呈現已確認的網路狀態，不是只有施放者看得到。視覺 Prefab 不需加入 Fusion Network Prefab 表。

## 2. 十項圖示：映射與匯入

來源目錄為 `deliverables/AbilityIcons_E_20261003_v4/`；Unity 目標目錄為 `Assets/_Project_Assets/UI/AbilityIcons/E_V4/`。Definition 目錄為 `Assets/_Project_Assets/Data/PlayerAbility/Active/`。

| 技能 | Definition 檔名 | 圖片檔名 |
|---|---|---|
| 爆破榴彈 | `Grenade.asset` | `01_ExplosiveGrenade.png` |
| 貫穿砲 | `PiercingCannon.asset` | `02_PiercingCannon.png` |
| 衝擊扇波 | `Shockwave.asset` | `03_ImpactFanWave.png` |
| 快速護盾 | `Shield.asset` | `04_QuickShield.png` |
| 治療包 | `HealingPack.asset` | `05_HealingPack.png` |
| 彈射彈 | `Ricochet.asset` | `06_RicochetBullet.png` |
| 經驗增加 | `Experience.asset` | `07_ExperienceBoost.png` |
| 閃現 | `Blink.asset` | `08_Blink.png` |
| 子彈時間 | `BulletTime.asset` | `09_BulletTime.png` |
| 精準鎖敵 | `PrecisionLock.asset` | `10_PrecisionLock.png` |

在非 Play Mode 執行 `Tools/Player Ability/套用指定 V4 技能圖示`，入口為 [`ActiveAbilityIconSetup.ApplyIcons`](../../Assets/Scripts/Editor/ActiveAbilityIconSetup.cs)。此入口會把上述十張來源圖覆寫複製到目標目錄，再把各 Definition 的 `hudIcon` 指向相同的 Sprite；它不是「只補缺」工具。需要換成其他正式圖示時，直接修改相應 Definition 的 `Hud Icon`；不要再次執行 V4 匯入而覆蓋人工選擇。

工具設定為 `Texture Type = Sprite`、`Sprite Mode = Single`、`Alpha Is Transparency = true`、`Generate Mip Maps = false`、`Max Size = 1024`、`Compression = None`。這些只影響圖示，不改技能數值。工具也會在缺少時建立調色設定資產，已有設定不覆寫。

手動配置順序：確認十份 Definition 已存在 → 將 PNG 匯入上述 UI 目錄並核對 Sprite 設定 → 把 Sprite 拖到相應 Definition 的 `Hud Icon` → 儲存。技能槽沿用置中與 `preserveAspect`；無圖示時新主動技能使用名稱前兩字。驗收時同時查看裝備槽與獎勵卡，避免只改其中一處。

## 3. 中央下方技能時間條

Owner 為 `StageHUD.prefab` 的 `ReferenceFrame/BattleAbilityHUD` 上的 [`LocalPlayerBattleAbilityHUD`](../../Assets/Scripts/UI/Player/LocalPlayerBattleAbilityHUD.cs)。新增根物件為 `TimedAbilityBar`，子物件為 `Fill`、`Name`、`State`、`Seconds`。沿用 [154](154_戰鬥HUD視覺配置第一版.md) 的 1920×1080 參考平面。

| Inspector 欄位（序列化名稱） | 配置／作用 |
|---|---|
| Timed Ability Root (`timedAbilityRoot`) | `TimedAbilityBar`；未接線時只保留原技能槽 |
| Timed Ability Fill (`timedAbilityFill`) | `Fill` 的 Image；Simple 模式、左側 pivot、零 offset，以 `anchorMax.x` 顯示剩餘比例 |
| Timed Ability Name (`timedAbilityName`) | 技能 Definition 的顯示名稱 |
| Timed Ability State (`timedAbilityState`) | 「施放準備」「待命」「效果持續」 |
| Timed Ability Seconds (`timedAbilitySeconds`) | 剩餘秒數，顯示一位小數及 `s` |
| Casting Bar Color (`castingBarColor`) | 施法延遲；預設 RGBA `(1, 0.78, 0.2, 1)` |
| Armed Bar Color (`armedBarColor`) | 閃現待命；預設 `(0.3, 0.8, 1, 1)` |
| Active Bar Color (`activeBarColor`) | 自身作用階段；預設 `(0.25, 1, 0.9, 1)` |

新建根物件預設 anchor `(0.5, 0)`、pivot `(0.5, 0.5)`、anchored position `(0, 280)`、大小 `400×18`，即參考畫面中央、由上方算約 y=800。名稱在條上方左側、秒數在上方右側、狀態在下方左側；TMP 使用現有 `NotoSansTC-Bold HUD SDF`，名稱／秒數 22、狀態 18。作者可直接調整根物件位置與大小；不移動準心、換彈提示或鈎索圓環。

顯示比例是 `Clamp01(剩餘秒數 / 本階段總秒數)`。總長由技能的 Networked `PhaseDurationSeconds` 提供；晚加入不會把首次看到的剩餘時間當成完整時間。施法階段、閃現第一次 E 的待命階段、快速護盾／經驗增加／子彈時間／精準鎖敵的 Active 階段可顯示。一般冷卻仍由原技能槽顯示；地上治療包等待拾取、閃現衝刺安全期限及舊空中技能不冒充中央自身效果條。效果到期、死亡、玩家缺失或卸裝後隱藏。

### 接線步驟

1. 非 Play Mode 執行 `Tools/Purgers/UI/Add Active Ability Timer To Stage HUD`，即 [`ActiveAbilityHudSetup.ApplyStageHudPrefab`](../../Assets/Scripts/Editor/ActiveAbilityHudSetup.cs)。此入口補元件與缺失引用，並儲存 `Assets/Prefabs/UI/StageHUD.prefab`。
2. 開啟 Prefab Mode，確認上述五個引用。新建時採用預設位置；重跑不重設已有根物件及文字的人工座標，也不覆寫已有有效引用。
3. 調整 `TimedAbilityBar` RectTransform，確保 Name／State／Seconds 不重疊鈎索圓環與現有情境提示。Fill 不使用沒有 Sprite 就無效的 Filled 模式。
4. 儲存 Prefab。在實際 StageHUD instance 檢查是否有 override 導致沒有繼承新增引用；不要用整版排版重建來消除人工設定。

## 4. 淡金護盾與整格生命

2026-10-04 使用者確認新版後，本節取代先前白色部分格方案。配置／所有欄位詳見 [167 已確認 HUD V2](167_已確認HUD美術與整格血量.md)。

LocalPlayerHealthSlider.healthSegmentView 指到原生命 Slider 底下的 LocalHealthSegmentView；只讀本機 PlayerShieldAbility.CurrentShield，透過 SetShieldHealth 傳給既有繪圖元件，不另建 HP Owner。

- 生命米白、護盾淡金 #D5C45E；護盾緊接目前可見生命格，同一基線及間距，不跨過空生命格。
- healthPerSegment=20；8＋7＋5 的真實損失到第三次才減一整格。治療同理，反向變化先抵銷顯示餘額。
- 初次快照／新護盾採完整尾格：25 盾顯示兩整格，消耗累積 20 後剩一格，歸零清除。HUD 數字仍即時顯示真實值，沒有增加盾量。
- 420×26 的固定 Rect；格寬18、間距3、斜角4，高容量自動縮窄。segmentRise=0、segmentOutline 空，保留舊序列化名稱。
- 破盾、到期、死亡或解除綁定清除護盾；重綁 ResetPresentation。低血量只改生命色。
- 舊 Connect Shield Cells In Loaded Scenes 工具只補來源引用；新版美術已套用，不需重跑舊版配置。後續直接在 Inspector 微調，勿整版覆寫。

## 5. 四色 Lift／Gamma／Gain 調色

正式設定資產是 [`Assets/Resources/Presentation/ActiveAbilityColorGrading.asset`](../../Assets/Resources/Presentation/ActiveAbilityColorGrading.asset)，類別為 [`AbilityColorGradingSettings`](../../Assets/Scripts/Player/Ability/Active/AbilityColorGradingSettings.cs)。`PlayerAbilityColorGrading.settings` 可選擇覆寫引用；留空依 Resources 路徑 `Presentation/ActiveAbilityColorGrading` 載入；資產缺少時程式暫用相同 C# 預設。

| 欄位 | 目前資產值／有效範圍 | 行為 |
|---|---|---|
| `fadeSeconds` | 0.25 秒；至少 0.01 | 子彈時間、精準鎖敵、經驗增加各自淡入及淡出時間，使用本機未縮放時間 |
| `healingPulseSeconds` | 0.45 秒；至少 0.01 | 一次成功回血的完整淺綠 pulse，並非淡入、淡出各 0.45 秒 |
| 每色的 `strength` | 0～1 | 0 關閉該顏色，1 使用完整設定，不停用技能 |
| 每色的 `lift` | Vector4，無 Inspector 數值上下限 | 陰影色偏；xyz 為顏色，w 為亮度偏移 |
| 每色的 `gamma` | Vector4，無 Inspector 數值上下限 | 中間調色偏；w 同樣不是 alpha |
| 每色的 `gain` | Vector4，無 Inspector 數值上下限 | 亮部色偏；w 同樣不是 alpha |

LGG 的中性 Vector4 是 `(1,1,1,0)`；不是 `(0,0,0,0)`，也不是 RGBA 的白色。先用 `strength` 調整可見度，再小幅調 xyz；沒有自動曝光或透明色罩。技能色偏加在既有 Volume 混合後的基底上，避免清掉場景原有 LGG。

| 效果／資產區塊 | Lift | Gamma | Gain | Strength |
|---|---|---|---|---:|
| 子彈時間／`bulletTime`：紫色 | `(1.04,.98,1.08,0)` | `(1.08,.94,1.16,0)` | `(1.06,.97,1.12,0)` | 0.8 |
| 精準鎖敵／`precisionLock`：藍綠 | `(.97,1.04,1.05,0)` | `(.94,1.10,1.10,0)` | `(.97,1.06,1.08,0)` | 0.7 |
| 經驗增加／`experience`：黃 | `(1.04,1.03,.98,0)` | `(1.09,1.07,.94,0)` | `(1.08,1.06,.98,0)` | 0.7 |
| 成功回血／`healing`：很淺綠 | `(.99,1.025,.995,0)` | `(.98,1.06,.99,0)` | `(.99,1.04,1,0)` | 0.4 |

子彈時間讀固定施放名單，因此受影響的本機隊友也顯示紫色。精準鎖敵、經驗增加讀自己裝備技能的 Active 狀態。回血只聽正式 `PlayerHealth.LocalHealingReceived`：State Authority 成功恢復正數生命後，用 RPC 通知該玩家 Input Authority；出生、Reset、滿血或失敗治療不觸發。Pulse 在 0 秒及 0.45 秒為 0，0.225 秒達峰值；期間再成功回血會從新事件時間重新起算。

多種顏色同時存在時，以各自轉場權重乘 strength；總權重大於 1 時正規化，避免無限累加。`PlayerAbilityColorGrading` 建立只含 LGG 的暫時 Global Volume，只在現有 `CameraFollow` 相機堆疊最後一個已開啟後製的 Game Camera（全部未開後製時選末台） 渲染時啟用。保留 authored Profile；若暫時開啟後製，該次渲染結束便還原。需要該相機具有 `UniversalAdditionalCameraData` 且 `Volume Layer Mask` 非空；不要另掛第二個常駐色罩或修改全域 `Time.timeScale`。

舊 `PlayerBulletTimeOverlay` 現只保留相容 Bind 入口並轉交 LGG Owner，不再建立 Canvas 色罩。舊技能上的 Overlay 顏色／不透明度欄位不再是這套四色的設定位置；調色應改此 Resources 資產。這次的子彈時間顏色為紫色，不沿用較早草案的藍綠 Overlay。

手動調整順序：選取上述設定資產 → 先保持 LGG 中性亮度 w=0 → 單獨觸發一種效果並改該組 strength → 再調 lift/gamma/gain 的 xyz → 儲存。建議從表內目前值開始，不以此調整投射物／UI 材質。正式多人執行的視覺資產各端應使用同一版本；數值沒有寫入玩家存檔或由 Host 同步覆寫本機資產。

## 6. 投射物模型與外觀朝向

配置對象在 `Assets/Prefabs/AbilityRuntime/Active/`：`GrenadeProjectile.prefab`、`HealingPackProjectile.prefab`、`RicochetProjectile.prefab`。三者 Root 保留 `PlayerAbilityProjectile`、`NetworkObject`／`NetworkTransform`；[`PlayerAbilityProjectilePresentation`](../../Assets/Scripts/Player/Ability/Active/PlayerAbilityProjectilePresentation.cs) 也掛在 Root，但所有視覺位移只套到 `visualAnchor`。

| Presentation 欄位 | 目前值／有效範圍 | 用法 |
|---|---|---|
| `visualAnchor` | `外觀定位` 子物件 | 必須在投射物 Root 之下，不能指 Root，整個子階層只能有視覺 |
| `visualPrefab` | 空白 | 空白沿用 Anchor 下可於 Prefab Mode 看到的模型；指定合法視覺 Prefab 後，本機建立替換模型並隱藏既有 Renderer |
| `trajectoryFollowSharpness` | 18；至少 0 | 榴彈／彈射彈朝向追隨速度切線；越大越快，0 立即對齊 |
| `modelRotationOffset` | `(0,0,0)` 度 | 榴彈／彈射彈非 +Z 模型的純外觀修正，不旋轉權威 Root；治療包不套用此欄位 |
| `healingFloatHeight` | 0.2 公尺；至少 0 | 治療包停留後基本抬升量 |
| `healingFloatAmplitude` | 0.06 公尺；至少 0 | 上下振幅；0 停止上下擺動 |
| `healingFloatFrequency` | 1 次／秒；至少 0 | 漂浮週期；0 時保留固定抬升、不擺動 |

模型彈頭約定朝 **本地 +Z**。榴彈異色、較寬彈頭在前，小尾段在後；沿拋物線切線轉向呈現重頭輕尾，沒有額外剛體重量或扭矩。治療包只在 `IsResting` 時抬升及漂浮，預設可見高度相對原模型為 `0.2 + sin(時間 × 2π) × 0.06` 公尺，範圍約 0.14～0.26；真正拾取中心及碰撞仍在權威 Root，不能靠漂浮外觀推高拾取範圍。

`Tools/Player Ability/補齊 E 技能投射物外觀接口（保留人工設定）` 對應 [`PlayerActiveAbilityVisualSetup.CreateMissingPresentationAssets`](../../Assets/Scripts/Editor/PlayerActiveAbilityVisualSetup.cs)。它只建立缺少的 `Visuals/GrenadeVisual.prefab`、`HealingPackVisual.prefab`、`RicochetVisual.prefab` 與 URP Unlit 示意材質。只有符合已知舊 `示意外觀` 球體／方塊結構的投射物才自動升級；已掛 Presentation 的物件保留作者設定，自訂舊模型不符合條件時警告並跳過。工具不配置爆炸／砲擊／扇波正式 VFX。

### 手動替換步驟

1. 開啟相應投射物 Prefab，在 Root 下建立或沿用 `外觀定位`，局部位置與旋轉先保持零、scale 1。
2. 將視覺模型放在 Anchor 下，或把視覺 Prefab 指到 `visualPrefab`。整個視覺階層不得含 `NetworkObject`、`Collider`／`Collider2D`、`Rigidbody`／`Rigidbody2D`；停用的子物件也會被檢查。
3. 把 Anchor 指給 `visualAnchor`。模型前端調成 +Z；榴彈／彈射彈需軸修正時改 `modelRotationOffset`；治療包直接調模型子物件。不要改投射物 Root 的方向。
4. 榴彈／彈射彈可從 sharpness 18 開始；治療包可從高度 0.2、振幅 0.06、頻率 1 開始。調整比例只能改模型／Anchor，不以此調整傷害半徑、重力或速度。
5. 儲存 Prefab。Play Mode 比對 Root 軌跡、命中位置與可見模型：漂浮／轉向只能改外觀；停留結束或重用時不得累積偏移。

## 7. 爆炸、貫穿砲與扇波的 VFX 插槽

2026-10-04 局部配置：`GrenadeProjectile.prefab` 的 `impactVfx.prefab` 已指定 `Assets/_Project_Assets/VFX/Mirza Beig/Cinematic Explosions FREE/Prefabs/Explosions/Explosion FREE 2 Variant.prefab`，`lifetimeSeconds=7` 秒（粒子 duration 5 秒，加最長粒子 lifetime 2 秒）。Anchor 留空，在 Host 確認的 ImpactPosition 播放；沿用既有各端 Render 一次性呈現，實例不附著投射物，投射物消失後仍可播完。未改傷害、半徑或特效素材尺寸。Editor 確認純視覺相容、引用有效、投射物 Root 無 Missing Script；聚焦 EditMode 15/15 通過。自然命中視覺與獨立 Host／Client 仍待實測。

共用 [`PlayerAbilityLocalVfxSlot`](../../Assets/Scripts/Player/Ability/Active/PlayerAbilityLocalVfxSlot.cs)，各插槽具備以下欄位：

| 欄位 | 預設／限制 | 作用 |
|---|---|---|
| `prefab` | 空白；只能是純視覺 Prefab | ParticleSystem／Animator 或自訂視覺腳本；禁止所有上述網路與物理元件 |
| `anchor` | 空白 | 可選 Root 之下的定位子物件，取相對位置與旋轉套到已確認的施放原點／方向；空白使用確認位置 |
| `lifetimeSeconds` | 2 秒；至少 0.05 | 本機實例自動清除時間；應涵蓋播放長度，與傷害、冷卻和 NetworkObject 存活不同 |

| 開啟的 Prefab／元件 | 插槽 | 空白時呈現與相關欄位 |
|---|---|---|
| `GrenadeProjectile.prefab`／`PlayerAbilityProjectile` | `impactVfx` | 爆炸示意線環；`impactVisibleSeconds=0.25` 秒，至少 0.05；`impactColor=(1,.65,.12,1)` |
| `RicochetProjectile.prefab`／`PlayerAbilityProjectile` | `impactVfx` | 命中示意線環，同上；此插槽不是每次撞牆反彈的事件 |
| `PiercingCannon.prefab`／`PlayerPiercingCannonAbility` | `fireVfx` | 示意直線；`beamVisibleSeconds=0.15` 秒、至少 0.01；`beamWidth=0.08` 公尺、至少 0.001；`beamColor=(.3,1,.95,1)` |
| `Shockwave.prefab`／`PlayerShockwaveAbility` | `fireVfx` | 空白不建立扇波特效；`visualEventSeconds=0.5` 秒、至少 0.05，控制本次呈現事件接收期限 |

有效自訂爆炸／砲擊 VFX 會取代該次示意線環／直線，不同時疊播兩套。治療包雖沿用同一投射物元件，拾取本身不是 `impactVfx` 的觸發條件；不要把該欄位當成已存在的拾取特效入口。

每次 VFX 以已確認序號／命中旗標在各端播放一次，逾期的砲擊與扇波不補播歷史事件。本機 VFX 不掛在投射物或技能 Runtime 底下，播放後以自己的 lifetime 清除；因此技能卸下或投射物消失，不會把已開始的粒子直接砍掉。接收事件窗口短於粒子播放時間是正常配置；提高 lifetime 不會延長傷害或擊退。

手動接線：

1. 製作前向 +Z 的純視覺 Prefab，檢查包括 inactive 子物件在內的全部階層都沒有 Collider、Rigidbody 或 NetworkObject。
2. 在上表指定的 Prefab 根元件展開相應插槽，拖入 `prefab`，將 `lifetimeSeconds` 設為粒子播放與尾跡收完所需時間，例如 1.2 秒特效可先設 1.5～2 秒。
3. 需要槍口或爆炸偏移時，於同一 Root 下建立純定位子物件並指給 `anchor`；不要移動 Root 來對齊特效。
4. 直線長度或扇波尺寸需隨結果調整時，在 VFX Prefab 的 MonoBehaviour 實作 `IPlayerAbilityLocalVfxReceiver.ConfigureAbilityVfx(origin, endPosition, radius, halfAngleDegrees)`。貫穿砲收到的是已被阻擋物截斷的終點；扇波收到範圍與半角。此接口只配置視覺，不能再次查命中並造成傷害。
5. 儲存後分別測試有命中、沒命中、近牆、卸裝後播放及到期清除。沒有正規特效時，保留空插槽即可沿用上表的示意行為。

## 8. 建議驗收順序與紀錄

下列均為待執行清單；不可只憑有新增檔案或有 Prefab 引用就填「通過」。

1. **編譯與 Console**：等 Unity 完成匯入及 Fusion weaving；確認沒有本次新增的 compile error／warning、Missing Script 或序列化引用失效。
2. **聚焦 EditMode**：執行 `ActiveAbilityHudTests`、`AbilityColorGradingTests`、`ActiveAbilityPresentationTests`，記錄非零 total、passed／failed；再執行完整 `PurgersRegression`，既有失敗分開記錄。
3. **中央條與圖示**：逐項核對十張圖的 Definition／獎勵卡／技能槽映射；將施法延遲設得足夠可見，確認時間條逐步耗盡。閃現第一次 E 顯示待命、第二次 E 後隱藏；治療包落地期間不顯示假的自身 buff。
4. **護盾**：最大生命 100、每格 20 時取得 25 護盾，應在可見生命格後看到兩格淡金完整格。消耗累積 20 才減一格；盾歸零清除。破盾、到期、死亡／重生與換裝不殘留；低血量只改生命色。
5. **四色**：分別觸發子彈時間紫、鎖敵藍綠、經驗黃與成功回血淺綠；核對 0.25 秒淡入／淡出與 0.45 秒回血 pulse。測滿血治療、出生及 Reset 不閃綠；重疊顏色、死亡、觀戰及離開效果後不得留色，也不得改寫場景 Profile。
6. **模型與 VFX**：榴彈彈頭沿下降切線、治療包只漂浮外觀、碰撞與拾取位置維持原玩法。自訂 VFX 播放一次，尺寸依確認資料，離開技能 Runtime 後能播完並清除。
7. **解析度與人工設定**：至少核對 1920×1080 與另一種比例。中央條避開鈎索圓環及準心／換彈提示；白盾向右延伸時不遮重要資訊。重跑接線工具後人工位置／已有引用／自訂模型數值應保留；V4 圖示匯入的覆寫行為另依第 2 節處理。
8. **獨立 Host／Client**：確認每位玩家只看到自己的 HUD、護盾及畫面調色，而世界 VFX 在需要的各端顯示。晚加入效果中途時，時間條讀完整總長與當前剩餘比例；逾期世界事件不重播。再測重生、離線及重新綁定，不以 Single Runner 結果替代獨立多人證據。

## 9. 十項技能 Gizmos（試行，等待使用者畫面驗收）

本輪只把 Gizmos 加到這十項 E 技能及其投射物。尚未把「所有技能都必須遵守」寫成專案通用規則或專案記憶；待使用者確認顯示效果後再決定。舊空中緩速／空中衝刺不變。

### 設定與開關

1. 開啟 Scene View 的 **Gizmos**。若腳本項目曾被關閉，在其下拉選單開啟對應的 Player…Ability／PlayerAbilityProjectile。
2. 全部 E 技能的 Editor 總開關：**Tools → Player Ability → E 技能 Gizmos → 顯示 Gizmos**。預設開啟，記在本機 EditorPrefs，不修改 Scene、Prefab 或玩法。
3. 開啟 Assets/Prefabs/AbilityRuntime/Active/ 中對應的技能 Prefab，選 Root 上的技能元件，展開 **技能範圍 Gizmos（僅 Editor 顯示） → Range Gizmos**。不需新增物件或接線。
4. 預設僅選取時顯示。選 Runtime、其父層或 Play Mode 有效 Owner 玩家即可查看；取消「僅選取時顯示」後可常駐。各技能的線、範圍與文字使用同一辨識色。
5. 執行中的投射物沿用 Launcher 技能的顯示設定；Launcher 不存在時才用投射物自己的設定。Prefab 未 Spawn 不讀網路狀態。

| Inspector 欄位 | 預設／有效值 | 作用 |
|---|---|---|
| 顯示技能 Gizmos | 開 | 該技能線、球與文字總開關 |
| 僅選取時顯示 | 開 | 防止多名玩家的技能讓場景擁擠 |
| 顯示文字 | 開 | 技能名、半徑／距離／角度、預覽限制；附深色底板 |
| 顯示路徑 | 開 | 投射路徑、射線、擊退及衝刺方向；關閉保留範圍與文字 |
| 預覽目前場景碰撞 | 開 | 只讀所屬 PhysicsScene；關閉、Prefab Mode 或 Preview Scene 明確不查詢物理，不回退到預設 PhysicsScene |
| 投射路徑預覽秒數 | 3；0.1～10 秒 | 上限仍受投射物剩餘存活限制；截斷端點不是爆炸／落地點 |
| 自訂技能顏色／線與文字顏色 | 關／白 | 開啟後以自訂 RGBA 同時覆寫線與文字；不改 VFX／HUD |
| 文字大小 | 13；10～24 | Editor 文字尺寸 |
| 文字位置偏移 | 世界座標 (0,0.3,0) 公尺 | 只移動標示，不改技能起點 |

這些是新增欄位的預設；程式即時讀取既有序列化技能數值與人工 override，不重存或重設 Prefab。

### 十項範圍與辨識色

| 技能 | 辨識色 | 本輪既有 Prefab 設定與圖形 |
|---|---|---|
| 爆破榴彈 | 橘 | 初速 40m/s、碰撞球 R 0.1m、爆炸候選球 R 10m；拋物線與預估接觸中心。球內仍受牆遮擋 |
| 貫穿砲 | 桃紅 | 最大射線 60m；目前場景的敵人可穿透、玩家／世界阻擋。線寬不是傷害半徑 |
| 衝擊扇波 | 紅 | R 5m 的三維球距與水平 ±60° 交集；額外 4m 擊退箭頭只是方向示意，並非延伸命中範圍 |
| 快速護盾 | 白 | 自身標記與 25% 最大生命、1.5 + (Lv−1)×0.5 秒；沒有虛構範圍球 |
| 治療包 | 綠 | 初速 18m/s、碰撞 R 0.1m、實際拾取 R 0.4m、存活 10 秒；拾取球不跟漂浮模型移動 |
| 彈射彈 | 藍 | 初速 60m/s、碰撞 R 0.1m、反彈路徑與 ×2／×4／×8 標記；第四次世界碰撞停止，命中特效線環不是 AOE |
| 經驗增加 | 黃 | 自身標記、持續 5 秒、經驗與承傷 ×2；沒有空間半徑 |
| 閃現 | 淺青 | 20m 前進方向、待命 5 秒；衝刺中顯示固定方向與剩餘行程，綁定有效 KCC 時標角色體積 |
| 子彈時間 | 紫 | 初始名單 R 12m；作用中留在施放快照中心，不隨玩家移動；期間改半徑不重抓名單 |
| 精準鎖敵 | 藍綠 | 設定上限 60m、三維半角 ±8° 的球面邊界；綁定遠程武器後取技能／武器較短射程與真正射擊原點 |

所有距離用世界公尺，Prefab 的 Scale 不會放大判定範圍。Prefab 尚未綁玩家時標示「配置預覽」並使用自身 Transform；Play Mode 使用有效 Owner/KCC／武器的正式起點及朝向，避免獨立 Runtime Root 留在生成點。

投射物的真實碰撞半徑、拾取半徑、重力與剩餘時間只有 State Authority 具有完整初始化資料；Proxy 只顯示提示，不假造物理範圍。預估使用當前靜態碰撞情況，沒有取代正式傷害／受擊接受、Fusion Lag Compensation 或將來的移動。出生排除會在離開 Owner 後解除，反彈不丟掉同一步剩餘距離。閃現只顯示路程上限與 KCC 體積，是否提前停止仍由正式 KCC 碰撞判定。

### 手動驗收

- 在 Prefab Mode 改一個範圍值，確認圖形與文字同步；移動／旋轉 Runtime 預覽時，確認以配置 Transform 顯示。
- 檢查單項總開關、僅選取、文字、路徑、碰撞預覽、自訂顏色，以及 Tools 總開關。Game View 需另外開啟自己的 Gizmos。
- Play Mode 選玩家後移動，確認預覽跟玩家而非 Runtime Root；子彈時間啟動後的快照球應保持原地。
- 選 State Authority 的飛行中／停留治療包，確認拾取球停在物理位置；子物件漂浮不搬動拾取球。
- 這份試行功能只提供 Editor 診斷，沒有修改技能傷害、冷卻、控制權或網路結果。自動化與畫面證據見 [Gizmos 驗證](Validation/AbilityGizmos/README.md)，執行中 Owner／投射物及獨立 Host／Client 實際畫面仍待驗收。

## 變更紀錄

- 2026-10-04：加入十項 E 技能的 Editor Gizmos 試行、顯示開關、辨識色與預覽邊界；通用規則與記憶等待使用者驗收。

- 2026-10-04：新增 E 技能圖示、中央時間條、護盾分格、LGG 四色、投射物純外觀與 VFX 插槽的配置／驗收手冊；只記錄來源核對與操作規則，未在本文件宣告驗證通過。


