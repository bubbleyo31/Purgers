# 原圖風格階級字體預覽

尚未套用正式 UI。原圖 I 是方正粗襯線：字形寬高約 430:750，直柱 180、上下橫槓厚 130。本預覽以幾何向量重建此比例，V 為同風格提案；不聲稱識別出原作者使用的字體。字體只包含空白、I、V，專供本階級範圍；不適合一般文字。

- PurgersRankSlabPreview.ttf：992 bytes，自製向量字形，build_font.py 可重建，無第三方字型依賴。
- 固定字級：沿用正式元件的全階級同字級演算法，較寬的 III 會讓全階級一起縮小。
- Adaptive：僅在隔離預覽中停用該文字元件的後續更新，以每個階級的實際字形等比適配同一字框；中心使用既有 Definition 和 SpritePointToLocal。此方案尚未寫入 Runtime。
- 兩版均保留原圖、完整 UV、圖示縮放、文字中心與彈匣水平中心。各預覽皆核對 42+42+9 次文字案例、105 次 mesh；中心誤差為零。已視看兩張正式尺寸圖。
- 原生 560×1120 總覽與 1080p／720p 卡片輸出供目視確認。未變更正式 Scene、Prefab、Definition 或 Runtime；無需把前一輪回歸結果當成本提案的實作證據。
- 字型的 Unity 匯入副本僅用於隔離渲染，預覽完成後由 AssetDatabase 刪除；可重新從此目錄複製 TTF 至 Assets/Editor/GrowthRankFontPreview 並匯入後重現。


## 已核准並套用

使用者已選定「粗襯線／各階適配」。正式字體、六個 UI 引用與 Runtime 排版已套用，最新驗證見 [SlabAdaptive](../../../Documentation/ProjectArchitecture/Validation/GrowthIcons/SlabAdaptive/README.md)；上述預覽紀錄保留。
