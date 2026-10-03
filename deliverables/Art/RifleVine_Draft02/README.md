# 藤蔓步槍與手臂：第二版，偏寫實材質＋可替換 ACOG 造型

日期：2026-09-25。使用者方向：降低手繪風格、往寫實迭代；瞄具先做 ACOG 2.5× 外觀與視線配置，倍率以後接入。

## 閱讀與開啟順序

1. [審閱頁](review.html)：整體、鏡體近照、手部、對軸畫面、換彈影片及第一版比較。
2. `RifleVine_Arms_Draft02.blend`：Blender 4.0.2 可編輯檔，貼圖已打包，含原六段動作。
3. `RifleVine_Arms_Draft02.fbx` ＋同名 `.fbm/`：整套手部、步槍與鏡體。
4. `ACOG_25_Module_Draft02.fbx` ＋同名 `.fbm/`：獨立無骨架瞄具，供後續替換與重新安裝。
5. `Textures/`：完整 PBR 貼圖，包含額外的 Unity MetallicSmoothness 圖。
6. `optic_setup.json`、`optic_validation.json`、`roundtrip_validation.json`：安裝座、鏡軸資料與驗證證據。

第一版與 Unity `rifle/V2` 內已匯入的資產保留。這次新增檔案只在 `deliverables/Art/RifleVine_Draft02/`。

## 相較第一版的變更

| 部位 | 第二版 |
|---|---|
| 木質外殼 | 深化木色，新增多尺度紋理、裂紋法線、粗糙度差異與少量表面起伏 |
| 藤蔓 | 降低飽和度和光澤，補細根、局部凹凸及表面細紋 |
| 金屬 | 區分槍身、鋼件與瞄具陽極表面，加入微小刮痕及粗糙度變化 |
| 手套 | 平滑原有手部拓撲，加入皮革表面、貼合手背的縫線與皮革／橡膠差異 |
| 袖子 | 改為較沉的黃色布料，加入織紋及小幅袖褶 |
| 瞄具 | 替換原簡單圓環，新增鏡身、目鏡護圈、調整鈕、固定座、螺栓、紅色導光條及透明鏡片 |

這是向寫實方向推進的可審閱第二版；依然保留原灰盒的部分槍身與手臂輪廓，還不是完成解剖、服裝剪裁與完整細節雕刻的最終寫實資產。表面貼圖為程序生成，並非實物掃描。原動畫的全部姿勢尚未完成穿模驗收。

## ACOG 外形與可替換邊界

外形參考 Trijicon ACOG 系列的鏡身、導光與鑄造底部輪廓。資料來源：[Trijicon ACOG Family Specification Sheet](https://www.opticsplanet.com/i/pdf/opplanet-trijicon-acog-family-spec-sheet-pdf.pdf)。該廠商資料的 TA31 欄列為 4×32；本稿依使用者需求以「2.5×」作為遊戲設計標記，**不宣稱是某個真實 2.5× 型號的精確復刻**。

- 全模型的鏡體位於 Blender Collection `ACOG_25_Replaceable`。
- 五個網格按用途分開：鏡身、鋼件、橡膠、導光條、鏡片。
- 全模型中這些網格全部使用原本的 `Root` 骨骼；沒有增加或重命名原骨骼。
- `ACOG_25_Module_Draft02.fbx` 則是沒有動畫與骨架的獨立網格，原點置於安裝座底部中心。
- 這版沒有真正的 2.5 倍放大、RenderTexture 光學鏡、遊戲準星、鏡內遮罩或視差模擬。
- `06_ADS_Axis.png` 的格線和紅色中心十字是審閱背景。它們未輸出到 FBX，也不是遊戲準星。

### 座標與視線

以下為 **Blender 中原 FBX 的靜止姿勢座標，單位公尺、Z 向上**，不可直接當作 Unity Inspector 的 Local Position。

| 項目 | X | Y | Z |
|---|---:|---:|---:|
| 獨立鏡體原點所對應的安裝座位置 | 0 | 0.028 | 0.0305 |
| 目鏡中心 | 0 | 0.104 | 0.0647295 |
| 物鏡中心 | 0 | -0.051 | 0.0647295 |
| 審閱用眼點 | 0 | 0.190 | 0.0647295 |

鏡軸方向為 `(0, -1, 0)`。保留原準星的中心高度；可用目鏡、物鏡兩點定義直線，再用原 `Root` 的動畫變換求各幀視線。開鏡末幀的 Blender 攝影機位置另存於 `optic_setup.json`。

這只確認模型本身的軸線與通道。現有 Unity 相機、ViewModel Root Offset、ADS Additive Layer 與 FOV 仍需在實際 Prefab 核對。既有 `FirstPersonViewModelAimAnimator` 與 `FirstPersonFovManager` 的責任不變。

### 後續更換鏡體

1. 在 Blender 開啟第二版，將骨架切到 Rest Position，展開 `ACOG_25_Replaceable`。
2. 隱藏該 Collection 檢查槍身、手部與動作仍可獨立存在。
3. 新鏡體以表中的安裝座為基準定位；優先保留同一鏡軸高度，避免直接改原動畫。
4. 若新鏡體改變軸高或目鏡距離，先重做開鏡對軸預覽，再決定 ViewModel Offset 是否需要正式遷移。
5. 匯入 Unity 後再確認座標轉換與骨骼實際姿勢；不能只依 Blender 數值盲填 Local Position。

## 材質設定

十組有貼圖的材質各提供四張圖，共 40 張。木質與藤蔓為 2048²；縫線為 512²；其餘為 1024²。鏡片與導光條另用參數材質。

| 圖片後綴 | Unity 設定與用途 |
|---|---|
| `_BaseColor.png` | sRGB 開啟，放 URP/Lit 的 Base Map，Tint 白色 |
| `_Normal.png` | Texture Type = Normal Map；初版 Normal 強度可用 0.7 |
| `_MetallicSmoothness.png` | sRGB 關閉；R = Metallic、A = 1 − Roughness；放 Metallic Map，Smoothness Source 選 Metallic Alpha，Smoothness 倍率 1 |
| `_Roughness.png` | sRGB 關閉；保留作 Blender／其他工具使用，不直接當成 Unity Smoothness |

`MetallicSmoothness` 的 G／B 通道為 0，它不是 AO 貼圖；也不是 glTF 的 MetallicRoughness 通道配置。

| 材質（輸出名稱以 `_R2` 結尾） | Metallic | 平均 Roughness |
|---|---:|---:|
| Bark_Walnut | 0 | 0.84 |
| Vine_Olive | 0 | 0.79 |
| Core_Charcoal | 0.55 | 0.56 |
| Trim_BrushedSteel | 0.88 | 0.38 |
| Sleeve_Ochre | 0 | 0.86 |
| Glove_Forest | 0 | 0.66 |
| Bracer_Graphite | 0.18 | 0.54 |
| Rubber_Soot | 0 | 0.91 |
| Optic_Anodized | 0.65 | 0.49 |
| Seam_Thread | 0 | 0.90 |

上述值皆為 0–1，貼圖另有局部變化。Unity 和 Blender 的環境光、曝光及色彩管理不同，需在遊戲實際光照下調整。

### 透明鏡片與導光條

- `Optic_CoatedGlass_R2` 是薄片透明外觀，Blender Alpha 0.09、Roughness 0.085、Metallic 0.15；未模擬真正透鏡倍率。
- Unity 需手動建立 Transparent URP/Lit 鏡片材質，低 Alpha 作起點，Smoothness 約 0.915，再核對前後兩片的排序與反射。FBX 不會自動建立完整 URP Shader 設定。
- `Optic_Fiber_Red_R2` 是紅色導光條參數材質，粗糙度 0.2，弱發光；Unity 的 HDR Emission 需依遊戲曝光另調，不能把 Blender 數值當成畫面一致保證。
- 後續做正式瞄準鏡 Shader 時可沿用鏡體／鏡片分離，替換鏡片材質與成像流程。

## 手動匯入與檢查順序

1. 將完整 FBX 與同名 `.fbm/` 複製至新的測試資產資料夾。`Textures/` 中的 MetallicSmoothness 圖也要一併帶入；FBX 沒有直接引用這些 Unity 專用通道圖。
2. Rig 使用 Generic，先關閉 Optimize Game Objects，核對原骨骼名稱和路徑。
3. Animation 頁確認 idle、shoot、Opening the scope、Reload.001、melee、Appear 六段。Blender 幀範圍是 1–61／1–2／1–4／1–63／1–24／1–11；起始幀慣例和 Unity 可能不同。
4. 建立／Remap 本版 `_R2` 材質。先看槍身與手部，再設定透明鏡片。
5. 在複製的 ViewModel 上測試，核對原 Animator、`WeaponViewModelReferences`、槍口與相機引用。
6. 檢查開鏡末幀：視線穿過目鏡與物鏡中心，鏡身不擋住中央；目前不應期待 2.5 倍放大或遊戲準星。
7. 依序播放 Idle、Shoot、開關鏡、Reload、Melee、Appear。注意支撐手指、護腕、鏡座和換彈手經過的位置。
8. 確認無 Missing Material／骨架引用／新增 Console 問題後，再決定正式資產替換；本輪沒有替你更動 Scene／Prefab。

## 驗證證據與限制

- 全模型 FBX 在 Blender 4.0.2 重新匯入通過。
- 原 63 根骨骼名稱、父子關係與六段動畫幀範圍一致。
- 逐整數幀比對 10,395 個骨骼世界位置，最大偏差約 `8.074e-7 m`（小於 0.001 mm）。
- 所有完整模型網格有 UV、材質、Armature modifier，沒有未蒙皮頂點或非有限座標；引用的貼圖存在。
- 獨立鏡體重新匯入為五個網格，沒有骨架／動畫，具有 UV 與材質。
- 鏡軸中心及半徑 8 mm 的八個周圍平行取樣線，共九條射線，未被不透明鏡體擋住。這不是完整視錐或眼盒驗證。
- 已檢視整體、鏡體前後近照、手套近照、ADS 對軸及換彈中間幀。
- Blender 最終建置沒有回報 Error／Warning；來源 FBX 重匯入仍可能有上一版記錄的舊貼圖／法線警告。
- 原 FBX SHA-256：`30dd4097b6e421d9d812b992ea3d203d21472a11826b6d15a4bbe9e758d44349`。
- 第一版 `.blend` SHA-256：`e2ffcd214e3e3f820e122bed0b652f51a0d04c8b5e1b58913adef84e811d0e82`，本版從此檔載入並另存。

尚未驗證：Unity 匯入後材質、實際 ADS Camera 對齊、Animator 混合、全動作穿模、多人／Play Mode。本輪只有外部美術資產迭代，未改 Runtime C#，未執行 PurgersRegression；對話中沒有可呼叫的 Unity MCP。

## 成本與可再調整處

完整資產目前 34,751 頂點、67,756 三角形、24 個網格物件。這版增加手部平滑與鏡體細節，尚未進行圖集、LOD 或 draw call 整理。

可優先審閱：ACOG 大小與位置、鏡座外形、藤蔓是否仍太規則、木殼是否保留太多灰盒平面、袖子與手套是否需要下一步重做服裝結構。這些外觀判斷通過後，再整理效能與正式 Unity 接線。

製作腳本：`scripts/build_draft02.py`；資產往返驗證：`scripts/validate_roundtrip.py`；鏡體驗證：`scripts/validate_optic.py`。重新建置會重寫第二版同名輸出，依賴第一版 `.blend` 與本機原 FBX 路徑。
