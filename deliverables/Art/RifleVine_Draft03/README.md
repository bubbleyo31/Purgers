# Attack Rifle — 已確認 Concept 02 模型交付（Draft03）

日期：2026-10-07。依本對話確認的 [Concept02](References/Approved_Concept02.png) 製作。新增原創短筒瞄具、青綠鏡片、圓弧後托與短聚合物彈匣；保留既有木紋藤蔓、前護木、握把、手臂與動作。實物只作外形特徵參考，沒有品牌標誌或精確複製型號。

## 開啟與交付內容

1. [模型審閱頁](review.html)：實際模型整槍、瞄具、後托彈匣、對側、四角面線框、ADS、換彈影片。
2. [完整 FBX](RifleVine_Arms_Draft03.fbx) ＋同名 `RifleVine_Arms_Draft03.fbm/`：手臂、武器、63 根骨骼、六段動畫。
3. [可編輯 Blender](RifleVine_Arms_Draft03.blend)：Blender 4.0.2，貼圖打包，保留四角面與原動作，另含審閱燈光／攝影機。
4. [獨立瞄具 FBX](Prism_Concept02_Module.fbx) ＋同名 `Prism_Concept02_Module.fbm/`：五個網格，無骨架／動畫；原點位於底座中心。
5. [資源統計](resource_report.json)、[FBX 往返驗證](roundtrip_validation.json)、[瞄具座標／鏡片參數](optic_setup.json)。

所有 `Previews/` 圖片及影片均為本交付 Blender 模型的實際渲染。唯一生成概念圖位於 `References/`，與模型成果分開標示。

**這是獨立美術交付，未複製到 Unity Assets，未替換 Scene／Prefab／Animator，也沒有更改遊戲倍率、彈匣容量、射擊或傷害。**

## 實作與拓撲

- 瞄具採短筒、斜口前罩、單一頂部旋鈕、側面橢圓控制鍵、低底座及木質下側飾；外殼與透明鏡片分離。
- 後托改用連續四角面環線，形成圓弧尾端、平順貼腮線及薄肩墊；剪除會突出新輪廓的舊木殼後段。
- 短彈匣使用四角面環線、六片分區凹面及低成本背脊紋路，全部綁定原 `magazine` 骨骼。
- 原手部網格、UV、頂點權重逐項雜湊一致；沒有重新細分、重綁或改動骨架。
- 槍身與藤蔓沿用來源幾何，因此模型仍保留原有前端長度、厚度與手指接觸位置。概念圖是外觀方向，不代表已重新調整握持與動畫。

| 範圍 | 頂點 | 四角面 | 三角面 | N-gon | 三角化後 |
|---|---:|---:|---:|---:|---:|
| 完整模型 | 28,397 | 27,293 | 20 | 202 | 55,748 |
| 本輪新增部件 | 3,498 | 3,240 | 0 | 0 | 6,480 |
| 新瞄具（包含於新增） | 1,618 | 1,432 | 0 | 0 | 2,864 |

- 上一版完整模型 67,756 三角形 → 55,748，減少 **17.7%**。
- 上一版瞄具 16,040 三角形 → 2,864，減少 **82.1%**。
- 完整模型 25 個網格、12 個使用中材質；獨立瞄具 5 個網格／材質。這是資產統計，不能直接當作 Unity draw call。
- 舊網格仍有 20 個三角面與 202 個 N-gon；本輪新增部件全為四角面，不把整套資產描述成全四角面。
- 原 `立方体.021` 匯出後的三角化含 99 個近零面積三角形，已記錄為繼承問題；本輪新增部件為 0。未為清理該舊網格而擅自改寫原結構。
- 木紋／藤蔓 2048²；一般材質 1024²；縫線 512²。沿用既有貼圖，彈匣重用深色與橡膠紋理；沒有新增 4K 貼圖。詳細尺寸、槽數與分區成本見資源統計。
- 四角面與成本控制的預設規則已寫入根目錄 `AGENTS.md` 及 [開發工作流 §6](../../../Documentation/ProjectArchitecture/150_開發工具與AI_Agent工作流.md)。

## 驗證

- 實際輸出的完整 FBX 與獨立瞄具均重新匯入 Blender。
- 63 根骨骼名稱／父子關係、六段動作名稱／幀範圍相同。
- 比對 **10,395** 個整數幀骨骼樣本，同時檢查位置、旋轉與縮放基底。
- 最大位置差 **0.0000008074 m（約 0.00081 mm）**；最大旋轉／縮放基底係數差 **0.000006766**。
- 所有輸出網格有 UV／材質，完整模型沒有未蒙皮頂點或非有限座標，引用貼圖皆存在。
- 原手部網格／UV／權重完全一致。
- 瞄具中央及半徑 8 mm 周圍共九條平行射線無不透明遮擋。這是模型軸線檢查，不是完整眼盒、倍率或遊戲相機驗證。
- 已渲染換彈 1–63 幀影片，檢視 1／20／40／63 代表幀、整槍、瞄具與 ADS。手指與藤蔓局部接觸沿用舊版，未宣稱所有動作逐幀無穿插。
- Unity MCP 只讀核對**既有 V3 模型**為 Generic、Scale 1、六段 24 FPS 動作；當時 `scriptCompilationFailed=false`，Console 讀取 0 error／warning。這不代表新 FBX 已在 Unity 驗證。
- 原始 FBX 重新匯入會出現舊貼圖路徑指向資料夾與舊法線映射警告；新 FBX 往返未缺貼圖，與原始檔警告分開記錄。
- 未執行 PurgersRegression、Unity 新資產匯入／材質轉換、Play Mode、Host／Client、全動作碰撞或目標平台效能測試。本輪未更改 Runtime 程式。

## Unity 手動匯入順序

1. 在新的美術資料夾放入完整 FBX、同名 `.fbm/` 與 `Textures/`，保留原 V3 模型。
2. Model Importer：Scale Factor 1；Rig = Generic；初次核對時關閉 Optimize Game Objects。不要手動改 FBX 軸向與骨骼名稱。
3. Animation 頁核對六段：`idle` 1–61、`shoot` 1–2、`Opening the scope` 1–4、`Reload.001` 1–63、`melee` 1–24、`Appear` 1–11，24 FPS。Unity 的 Clip 名可能帶 `第一人稱手|` 前綴。
4. 材質頁 Extract／Remap 至獨立的新材質資料夾，不覆蓋舊版材質。依下表配置。
5. 在你自行複製的 Attack ViewModel 上替換模型，核對 Animator、骨骼 Root、`WeaponViewModelReferences`、MuzzlePoint、Aim Animator 與 WeaponCamera 引用。此交付沒有自動建 Prefab 或接線。
6. 依序測 Idle、Shoot、開關鏡、Reload、Melee、Appear；確認彈匣與紋路一起拆卸，手臂動作不變，木托／瞄具不擋中央視線。
7. 在實際遊戲光照與相機下檢查玻璃透明排序、正反面、鏡框反光、指尖／護木接觸，再決定正式替換。

### 不透明材質

| 輸入 | 設定 |
|---|---|
| BaseColor | URP/Lit Base Map；sRGB 開；Tint 白色 |
| Normal | Texture Type = Normal Map；強度以 0.7 為起點 |
| MetallicSmoothness | sRGB 關；R=Metallic、A=Smoothness；Smoothness Source = Metallic Alpha，倍率 1 |
| Roughness | 保留給 Blender；不能直接當 Unity Smoothness |

`Magazine_Polymer_R3` 使用 `Core_Charcoal_BaseColor`、`Rubber_Soot_Normal` 與 `Rubber_Soot_MetallicSmoothness`；**Metallic=0**，不可套用金屬機匣的金屬度圖。分區凹面共用 `Rubber_Soot_R2`。

### 青綠鏡片

指定至 `Prism_Prism_Teal_CoatedGlass_R3` 網格：

| 項目 | 起始值／效果 |
|---|---|
| Shader | URP/Lit |
| Surface Type／Blending | Transparent／Alpha |
| Base Color | Blender 線性 RGB (0.055, 0.255, 0.215)，約等於 sRGB `#428A80`；先用此色再依實際光照微調 |
| Alpha | 0.13，範圍 0–1；越高越不透，需同時檢查前後兩片疊加 |
| Metallic | 0.55，範圍 0–1；調整鍍膜反射外觀 |
| Smoothness | 0.87，範圍 0–1；對應 Blender Roughness 0.13 |
| Render Face | Both 作檢查起點；確認鏡片正反面及透明排序 |
| Cast Shadows | 關閉；避免鏡片投不透明陰影 |

以上為手動重建外觀的起始值，FBX 不能攜帶完整 Blender／URP Shader 行為。前後鏡片、環境反射與 Unity 透明排序仍需現場核對，不能保證渲染一鍵一致。沒有新增真正倍率、RenderTexture、準星、視差或 FOV 系統。

### 安裝座與光軸

`Prism_Concept02_Replaceable` 為完整模型內的獨立瞄具 Collection，綁原 `Root`。獨立 FBX 原點為底座中心。

以下為 Blender rest space、公尺、Z 向上，**不可直接貼入 Unity Local Transform**：

- 底座原點：(0, 0.027, 0.0305)。
- 目鏡中心：(0, 0.081, 0.0647295)。
- 物鏡中心：(0, −0.021, 0.0647295)。
- 光軸方向：(0, −1, 0)；沿用原步槍的光軸高度。
- 審閱眼點：(0, 0.19, 0.0647295)。

後續換鏡體優先保留光軸高度，先重做 ADS 渲染，再處理 ViewModel Offset；不要改寫原動作來掩蓋安裝錯位。

## 重建

使用 Blender 4.0.2，依序執行 `scripts/build_draft03.py`、`scripts/render_review.py`、`scripts/render_reload.py`、`scripts/validate_roundtrip.py`，再以 Blender 隨附 Python 執行 `scripts/package_delivery.py`。

建模入口依賴同層 `RifleVine_Draft02/RifleVine_Arms_Draft02.blend` 與它的 Textures；往返驗證另讀專案原始步槍 FBX。所有輸出僅寫入此 Draft03 資料夾。完整交付檔可獨立開啟，但重建腳本仍需要上述來源。

