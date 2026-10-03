# Menu 筆刷校正與子視窗範本 V2

核對日期：2026-10-03。此記錄只涵蓋本輪美術校正與本機預覽。

## Unity

- 共享筆刷 SHA256：5149B059018FC391FECC4C8F0090D61AED6A90C725FBED6AA90AEB751FECC57E。
- 原 GUID：3f61ec3ae1b90e147af1698831a055e3；1560 × 360，Full Rect，無壓縮，無 Mipmap。
- 六個 Button 的 RectTransform 位置／大小及 Persistent On Click 校正前後相同。
- 隔離 Preview Scene 渲染主畫面：[main-isolated.png](main-isolated.png)。此圖為複製主畫面的 Editor 視覺檢查，不是 Play Mode 證據；暫時預覽 Scene 已清理。
- 完整 Filled 高亮與 Simple 底圖的網格頂點及 UV 一致；未增加遮罩或點擊區域。
- 腳本編譯完成，scriptCompilationFailed=false；Console error/warning=0。
- 進入本輪時原 Scene 已 dirty，結束仍 dirty；未保存、未重載原 Scene，未啟動 Play Mode 或會自動保存 Scene 的 Unity Test Runner。上一輪 5/5 與回歸結果不能視為本輪重跑。
- 未修改任何子視窗 Scene 接線或 Photon 原始程式。

## 本機網頁

位置：C:/Users/hsuai/.codex/visualizations/2026/10/03/01a1005e-4346-7243-90f6-ebe781b100f3/purgers-menu/dist。

預覽：http://127.0.0.1:4173/。可用 ?view=settings、?view=continue、?view=party 直接開窗。左上 DESIGN 02 工具僅供審稿，不是新增遊戲入口。

- 已檢查：設定切換、未選存檔禁止開始、選檔更新摘要與啟用開始、取消刪除保留三列、確認刪除只移除示範列、空清單、無效代碼提示、ABC123 示範轉場、返回、玩家名称修改。
- JavaScript 語法檢查通過，瀏覽器 error/warn 記錄為空。
- 設定／繼續／多人截圖保存在預覽專案根目錄 settings-v2.jpg、continue-v2.jpg、party-v2.jpg。
- 所有欄位值、存檔與載入狀態皆示範；不讀寫 Unity 存檔，不連接 Photon，未发布網站。

來源與調整說明：[166](../../166_Menu筆觸視覺與配置.md)。