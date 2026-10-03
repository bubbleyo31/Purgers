# 藤蔓步槍與第一人稱手臂：外觀審閱初版 01

日期：2026-09-25。定位：可編輯、可播放原動畫的美術草稿，尚未整合到 Unity 正式 ViewModel。

## 先看這些檔案

1. `review.html`：設計圖、模型多視角、手部與換彈影片。
2. `RifleVine_Arms_Draft01.blend`：Blender 4.0.2 製作，貼圖已打包，包含六段動作與拍攝場景。
3. `RifleVine_Arms_Draft01.fbx`：模型、蒙皮、骨架與六段動畫；外部貼圖在同名 `.fbm` 資料夾。
4. `Textures/`：八組 512×512 BaseColor、Normal、Roughness 貼圖，共 24 張。
5. `roundtrip_validation.json`：FBX 重新匯入驗證；`build_report.json`：模型統計與來源雜湊。

## 這一版做了什麼

- 依使用者確認，沿用來源 FBX 比例、握持位置、骨架與動畫，補齊概念圖未畫出的厚度與背面。
- 沿用灰盒基礎形體，加入獨立有厚度的木片、枝節、藤蔓、卷鬚、槍口內襯及彈匣表面細節。
- 手部沿用原始手指拓撲與骨骼權重，加入黃色袖子、深色護腕、銀灰邊帶與深綠手套；袖子有少量輪廓調整。
- 木片與藤蔓主要由原 `Root` 骨骼驅動；彈匣飾條由 `magazine` 驅動；原 `Pull handle` 與 `trigger` 保留。
- 表面是程序生成的初版紋理與材質。尚未製作概念圖中的完整手繪筆觸、精修布料皺褶、手套縫線與磨損。

本次只新增 `deliverables/Art/RifleVine_Draft01/` 內的交付檔案，未變更原 FBX、`.meta`、Scene、Prefab、Animator Controller 或 Runtime C#。

## 可自行審閱的重點

| 項目 | 目前狀態 | 審閱重點 |
|---|---|---|
| 武器外形 | 以原 FBX 為基礎增加裝飾 | 木片厚度、藤蔓分布是否符合美術方向 |
| 正反面 | 側面依概念，背面自行補齊 | 背面細節與頂部裸露金屬比例 |
| 手臂 | 配色、護腕與邊帶已建立 | 袖子長度、手套深淺、護腕尺寸 |
| 動畫 | 六段來源動作已輸出 | 換彈、開鏡、近戰時裝飾與手部的間距 |
| 第一人稱畫面 | 提供 Blender 審閱鏡頭 | 此鏡頭不是 Unity 的正式相機／FOV |
| 貼圖 | 每種材質 512×512 可重複紋理 | 色彩方向；之後是否升級為獨立精修 UV 與貼圖 |

這版並非與概念圖逐筆一致的最終模型。藤蔓走向、木片形體及手部細節仍需美術審閱；不保證全部姿勢無穿模。藤蔓本身沒有新增擺動骨骼，隨武器剛性移動。

## 動畫與驗證

來源：`Assets/_Project_Assets/Models/Player/Profession/Attack/Materials/rifle/第一人稱_步槍_uv.fbx`。

| 動作 | Blender 匯入後幀範圍 | 原 Unity clip 範圍 |
|---|---:|---:|
| idle | 1–61 | 0–60 |
| shoot | 1–2 | 0–1 |
| Opening the scope | 1–4 | 0–3 |
| Reload.001 | 1–63 | 0–62（Unity 顯示名稱 Reload） |
| melee | 1–24 | 0–23 |
| Appear | 1–11 | 0–10 |

Blender 使用 24 fps；不同軟體的起始幀慣例不同。FBX 匯入器可能為動作加上骨架名稱前綴，請以動作名稱尾段識別。

已驗證：

- 匯出的 FBX 能在 Blender 4.0.2 重新匯入。
- 63 根骨骼的名稱與父子關係一致，六段動作與幀範圍一致。
- 比較所有動作的每個整數幀，共 10,395 個骨骼世界位置；最大差值約 `8.074e-7 m`，小於 0.001 mm。
- 所有輸出網格都有 UV、材質、Armature modifier；沒有未蒙皮頂點或非有限座標。
- FBX 引用的外部貼圖存在。
- 已檢視側面、斜角、持槍與換彈中間姿勢的實際模型渲染。
- 原 FBX SHA-256 仍為 `30dd4097b6e421d9d812b992ea3d203d21472a11826b6d15a4bbe9e758d44349`。

驗證限制：上述是 Blender 的資產往返檢查，不包含骨骼旋轉矩陣逐項比對、連續碰撞／穿模檢測、Unity 材質轉換、Animator 混合或 Host／Client 實測。本對話沒有可用的 Unity MCP，因此未執行 Unity 匯入驗收、Console 或 PurgersRegression；本次沒有遊戲行為程式變更。

來源 FBX 在 Blender 匯入時會回報舊貼圖路徑指向資料夾，以及法線格式警告。本稿使用新增材質與貼圖，輸出 FBX 的重匯入未再出現這些來源警告。原資產沒有為此被改寫。

## Blender 操作

1. 以 Blender 4.0.2 或相容版本開啟 `.blend`。
2. 按數字鍵盤 0 可看審閱攝影機；材質預覽或渲染模式可看貼圖。
3. 選擇骨架 `第一人稱手`，在 Dope Sheet → Action Editor 切換上述六段動作。
4. 依表格設定播放範圍，按空白鍵播放。檔案預設是 idle。
5. 木片與藤蔓在 `Detail_weapon_*` 網格；護腕新增部分在 `Detail_arms_*`。
6. 藤蔓與木片為可編輯網格；大量零件已按材質合併，Edit Mode 中可用游標下的 L 選取相連的單條藤蔓／木片。
7. 色彩由對應材質的 BaseColor 圖控制，粗糙度由 Roughness 圖控制。需要保留此次草稿時，另存下一版本。

## Unity 手動匯入與預期檢查

外觀審閱通過後再做以下步驟。FBX 自帶的 Blender 材質不等於已設定完成的 URP 材質。

1. 將 `RifleVine_Arms_Draft01.fbx` 和 `RifleVine_Arms_Draft01.fbm/` 一起複製到一個新的測試資產資料夾，保留兩者相對位置。
2. Model Importer → Rig 使用 Generic。確認骨架 `第一人稱手` 下仍有原骨骼路徑，先停用 Optimize Game Objects 以便逐項核對。
3. Animation 頁確認六段動作與長度，idle 開啟 Loop Time；其餘依原 ViewModel 設定。先用 Inspector Preview 檢查，再連接 Animator。
4. 建立八個 URP/Lit 材質。`*_BaseColor.png` 使用 sRGB，放到 Base Map，材質顏色保持白色；`*_Normal.png` 的 Texture Type 設 Normal Map，再放 Normal Map，初版強度可用 0.35。
5. `*_Roughness.png` 是粗糙度資料，sRGB 應關閉。URP/Lit 使用 Smoothness，不能把 Roughness 直接當 Smoothness；初步可使用下表常數。若要使用圖的局部變化，需要另行打包 Metallic R／Smoothness A，Smoothness = 1 − Roughness。
6. 在 FBX 的 Materials Remap 指定上述材質。用複製的測試 ViewModel 核對，不直接覆蓋現有 Player／職業 Prefab。
7. 保留既有 `WeaponViewModelReferences`、Animator Driver、槍口節點與相機設定。這些引用不會由新 FBX 自動遷移。
8. 按順序檢查 Idle → Shoot → 開鏡／關鏡 → Reload → Melee → Appear。預期：槍身與裝飾跟隨；彈匣與其飾條一起移動；扳機與拉柄保有獨立運動；手套、護腕跟手臂移動。
9. 特別檢查支撐手手指附近藤蔓、換彈手經過槍身的位置、袖口接縫及瞄準視線。任一處穿模先修外觀，避免為裝飾直接改動既有動畫。
10. 檢查 Console、新模型 Missing Material／骨骼引用及實際相機下的可見範圍；經確認後才考慮正式 ViewModel 遷移。

| 材質 | Metallic | Smoothness 初值 |
|---|---:|---:|
| Bark_Walnut | 0 | 0.16 |
| Vine_Olive | 0 | 0.14 |
| Core_Charcoal | 0.35 | 0.33 |
| Trim_BrushedSteel | 0.70 | 0.55 |
| Sleeve_Ochre | 0 | 0.09 |
| Glove_Forest | 0 | 0.08 |
| Bracer_Graphite | 0.20 | 0.36 |
| Rubber_Soot | 0 | 0.05 |

這些值的範圍皆為 0–1，僅影響本地材質表現；實際 Unity 光照與色彩管理需另行核對。

## 成本與後續整理

- 初版共 17,887 頂點、35,528 三角形、19 個網格物件、8 種材質。
- 這是審閱版本，尚未進行正式 draw call／蒙皮成本量測、合批、貼圖圖集、LOD 或目標平台效能最佳化。
- 手部使用原灰盒拓撲；指關節、袖子與護腕仍可看出部分低面數輪廓。
- 背面造型與粗糙度為本輪依原圖風格補齊，屬待確認設計。
- 製作與驗證腳本在 `scripts/`；重新建置需要本機 Blender 與原 FBX 路徑。`build_draft.py` 會重寫本草稿資料夾中的同名輸出，不會改原 FBX。
