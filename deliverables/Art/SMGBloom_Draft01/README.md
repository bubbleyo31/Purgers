# 衝鋒槍 SMGBloom — 美術初版

本版依使用者的衝鋒槍設計圖與 `第一人稱_衝鋒槍_uv.fbx` 製作，延續偏寫實材質方向。保留來源 FBX 比例、骨架與五段動作；未更動步槍資產、Unity Scene、Prefab、Animator 或遊戲程式。

## 先看這些

1. [審閱圖集](review.html)：側面、立體角度、花朵、瞄具、第一人稱與換彈。
2. [可編輯 Blender 檔](SMGBloom_Arms_Draft01.blend)：內含打包貼圖、原骨架及審閱攝影機。
3. [完整模型 FBX](SMGBloom_Arms_Draft01.fbx)：武器、手臂與動畫。搬移時保留同名 `.fbm` 資料夾。
4. [獨立反射瞄具 FBX](Reflex_Branch_Module_Draft01.fbx)：安裝座中心為原點，沒有骨架與動畫；保留同名 `.fbm` 資料夾。
5. [貼圖資料夾](Textures)：完整 PBR 貼圖，包括 Unity 用的 MetallicSmoothness。

所有預覽均為交付模型的 Blender 渲染。沒有把生成概念圖當成模型成果。

## 本版造型

- 深色金屬機匣、木質槍托及握把、立體枝根與纏繞綠藤。
- 主要展示側保留三朵淡粉色五瓣花：後機匣、前護木、握把。另一側依相同風格補齊。
- 藤蔓增加跨越頂部的連續段、末端捲鬚；彈匣底板與細肋跟隨 `magazine` 骨骼。
- 手臂沿用這份 FBX 的蒙皮，增加黃褐袖布、深色手套與護腕材質。
- 瞄具只參考 [FALKE LE 官方介紹](https://falke-germany.com/en/falke-le/) 的護罩式反射瞄具類型。另行設計雙層切角框、分離安裝腳、側面圓形控制件及木質飾片；沒有加入品牌或型號標誌。
- 本版瞄具為外觀及視線配置；未製作遊戲內準星、倍率、視差補償或鏡內渲染。

## 模組與座標

Blender Collection `Reflex_Branch_Replaceable` 包含整組可替換瞄具。完整模型中它使用原有 `Root` 骨骼；獨立 FBX 已移除骨架。

[optic_setup.json](optic_setup.json) 記錄安裝點、視線軸與審閱眼點，單位為公尺，座標系是 **Blender rest space**。不要直接把數值貼到 Unity Transform；FBX 軸向轉換、Prefab Transform 與現有 WeaponCamera 必須一起核對。

`06_Aperture_Axis.png` 是 Blender 對軸格線檢查，中央沒有加入實際準星。格線與燈光不在 FBX 中。

## 驗證與範圍

- FBX 往返重新匯入：**63 根骨骼、五段動畫**，名稱及父子關係一致。
- `idle`：1–61；`Opening the scope`：1–4；`Opening the scope shoot`：1–2；`Reload`：1–36；`shoot`：1–2。來源為 24 FPS。
- 每個整數幀比對骨骼位置，以及旋轉／縮放的 3×3 基底；共 **6,615 個骨骼樣本**。
- 最大位置差約 **0.00133 mm**；最大基底係數差約 **0.00000943**。
- 匯出網格檢查包含蒙皮、有效頂點座標、UV、材質與外部貼圖存在性。
- 詳細資料：[roundtrip_validation.json](roundtrip_validation.json)、[optic_roundtrip.json](optic_roundtrip.json)、[build_report.json](build_report.json)。
- 目前完整模型約 **99,476 triangles／50,302 vertices／18 個網格**。這是審閱版本，尚未製作 LOD、貼圖集或依實機量測整併材質。
- Unity MCP 本次只讀取**來源** FBX：Generic、Scale 1、五段實際 Clip；編譯旗標沒有失敗。新輸出的模型尚未匯入 Unity，因此不能把 Blender 檢查當成 Unity ADS、材質、Animator 混合或 Play Mode 通過。
- 已渲染換彈預覽；逐幀骨骼一致不等於所有裝飾與手指完全不穿插。正式接線後仍需從遊戲相機檢查握持、換彈及開鏡過程。
- 來源資料夾另有獨立的 `.anim` 資產；本次保留的是 FBX 內的五段動畫，未改寫或合併外部 `.anim`。

## Unity 材質對應（待審閱後手動整合）

使用現有 URP 材質流程，先在新的美術資料夾匯入模型與貼圖，再檢查 Model Importer 的材質對應。不要直接替換現有 Source FBX。

| 貼圖 | 用途 | 設定方向 |
|---|---|---|
| `*_BaseColor.png` | Base Map | sRGB 開啟 |
| `*_Normal.png` | Normal Map | Texture Type = Normal map |
| `*_MetallicSmoothness.png` | Metallic Map | sRGB 關閉；R=Metallic、A=Smoothness；Smoothness 使用 Metallic Alpha |
| `*_Roughness.png` | Blender／其他 DCC 粗糙度 | sRGB 關閉；不能直接當 Unity Smoothness 使用 |

木頭、藤蔓、布料、花瓣為非金屬。金屬設定與貼圖尺寸列在 `optic_setup.json`。木頭／藤蔓為 2K，其餘大多 1K，花蕊為 512。

鏡片 `Reflex_Lens_Coating` 是透明青綠鍍膜。FBX 仍不能完整攜帶 Blender／URP shader 的設定；Unity 中需另建透明材質並檢查 Alpha、排序與背面顯示。不要讓預設不透明材質封住視窗。

正式整合時依現有 `WeaponViewModelReferences`、Aim Animator 及 WeaponCamera 所有權設定；本版沒有建立另一套 FOV 或瞄準系統。

## 重建

使用 Blender 4.0.2：

```powershell
& 'M:/OD/Blender/blender.exe' --background --factory-startup --python-exit-code 1 --python 'M:/UnityProject/Purgers/deliverables/Art/SMGBloom_Draft01/scripts/build_smg.py'
```

接著執行 `scripts/validate_roundtrip.py` 與 `scripts/inspect_optic_export.py`。`scripts/render_reload_movie.py` 可產生完整換彈預覽。建模入口只寫入本交付資料夾；來源 FBX 路徑定義在建模腳本頂部。

