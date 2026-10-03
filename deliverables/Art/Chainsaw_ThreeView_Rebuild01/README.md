# 電鋸 — 原 FBX／新三視圖重建版 01

本版重新匯入使用者最初的「第一人稱__電鋸_uv.fbx」，以最新 2172×724 三視圖為外觀基準。前四版保留，不作為本次模型來源。

## 外觀

- 依 SIDE 重建楔形暗紅褐機殼、長薄灰白刀片、略向前加寬的大圓頭。
- 依 TOP 建立機身寬度變化、薄刀片及外圈層次；FRONT 用於核對刀片置中與握把開孔。
- 外圈為深橄欖褐色木質軌條與規則排列的短尖齒，保留明確切面。
- 直接擷取新三視圖可見菌絲線帶，建立實際低浮雕網格；不是把參考圖貼在模型上。
- 以最新三視圖為準，沒有紅菇、沒有舊版紅色刀面圖案。
- 原始手部網格位置與蒙皮權重完全保留；本版不重新設計手臂。

## 已確認的握把處理

使用者已選擇：「保留原動畫的手部位置，依原握持點調整新握把；機身與鋸片依三視圖建模」。

三視圖的握把投影互相不完全一致，後握把高度也與原 FBX 相差約 12 公分。因此前提把與後握把工作面依原握持位置適配，不能宣稱握把與 SIDE 像素位置完全一致。原始手部動作保持原樣。機殼內部依七段原動作的右手活動範圍挖出帶 5 mm 餘量的握持通道，前提把下端補足接殼支座。

## 交付檔案

- [含動畫 FBX＋貼圖包](Chainsaw_ThreeView_Animated_Package.zip)
- [FBX 主檔](Chainsaw_ThreeView_Animated.fbx)，移動時保留同名 .fbm 資料夾。
- [Blender 原始檔](Chainsaw_ThreeView_Animated.blend)
- [三視角、純幾何與動畫審閱](review.html)
- [原動畫往返驗證](roundtrip_validation.json)、[手部及檔案檢查](delivery_validation.json)
- [比例與建模處理](construction_report.json)、[參考紋路資料](reference_extraction.json)、[浮雕網格統計](relief_report.json)
- [完整模型統計](build_report.json)

## 保留與驗證

58 根骨骼、24 FPS、7 段原動作：attack1、attack2、attack3.001、attack4、idle、melee、partition。

idle 右掌／手指的 498 個檢查頂點在機殼內的數量已由 117 降為 0；此為特定姿勢與機殼的局部檢查，不代表全部遊戲動作無穿插。

輸出 FBX 重新匯入 Blender，逐整數幀比對 7,714 個骨骼位置／旋轉縮放基底樣本、名稱階層及動作範圍。另比對原始手部的頂點座標與全部權重。實際結果見 JSON 報告。

來源沒有獨立鋸鏈循環，木質外圈隨原 Root 動作。未合併主 FBX 以外的獨立出場 FBX／.anim。

沒有改動正式 Scene、Prefab、程式或原始 FBX。Unity 材質、Animator 混合、遊戲相機、逐動作穿插與效能尚待整合驗證；Blender 證據不等於 Unity 驗證。未執行 PurgersRegression，未製作 LOD。

## 材質

BaseColor 使用 sRGB；Normal 設為 Normal map；MetallicSmoothness 關閉 sRGB，R 為金屬度、A 為平滑度。Roughness 供 DCC 使用，不直接當作 Unity Smoothness。原始手部材質重新連結專案中原有的 Albedo／Normal 貼圖；未重新繪製。

## 重建

先用具 Pillow／NumPy 的 Python 執行 scripts/extract_reference.py，再以 Blender 4.0.2 執行 build_chainsaw.py、validate_roundtrip.py、validate_art.py、render_attack_movie.py，最後用 Python 執行 package_delivery.py。

