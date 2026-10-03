# 彈孔素材變體

2026-09-25 依使用者提供的 `Game_0514/Assets/General/2DOBJ/hole.png` 生成，使用內建 imagegen。每款獨立 RGBA PNG，外圍透明、中央黑洞不透明；沒有加入遊戲生成邏輯或材質接線。

| 素材 | 外觀 |
| --- | --- |
| BulletHole_01_Compact.png | 圓形凹洞與集中破損邊緣 |
| BulletHole_02_Radial.png | 不對稱剝落、多道放射裂紋 |
| BulletHole_03_Oblique.png | 斜向橢圓孔與兩端撕裂 |
| BulletHole_04_Fractured.png | 不規則大缺口與較重碎裂 |

## 生成提示

共同提示：One centered isolated game bullet-hole decal on a square transparent RGBA canvas. Deep opaque black recessed aperture, graphite-gray chipped ragged rim, sparse sharp radial cracks, restrained beveled shading, neutral grayscale. Orthographic straight-on view. No full wall or material swatch, no floating debris, smoke, fire, text, watermark, white background or baked checkerboard. Transparent exterior and opaque black center. Match reference style; ample clear margins.

變體提示：01 compact round hole, five asymmetric cracks; 02 irregular round hole, broader left-side damage and seven thinner cracks; 03 diagonal oval oblique impact with opposing tears; 04 polygonal-rounded aperture, missing upper-right chunk and wider crushed rim. 後三款另以 01 作風格參考。

檔案保留生成器原始尺寸與 alpha，尚未驗證遊戲內不同表面顏色上的融合效果。黑色凹洞為貼圖呈現的深度，不會在幾何上穿孔。

## 命中特效採用設定（2026-09-25）

`WeaponImpactSettings.asset` 使用 Compact、Radial、Fractured 三款 `.mat`，不採用 Oblique，原圖保留。新 `Purgers/BulletHole` URP Shader 提供透明世界表面貼片。Spark 引用 `Assets/Art/VFX/SparkOnWall_vfx/Spark_vfx.prefab`。Scene／Player Prefab 尚待手動掛載，詳見 [163](../../../../Documentation/ProjectArchitecture/163_槍械命中特效與彈孔.md)。
