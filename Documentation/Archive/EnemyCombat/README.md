# 舊敵人戰鬥程式交付封存

2026-09-17 從 `Assets/Scripts/Enemy/Combat` 移入兩份歷史 ZIP，避免 Unity 匯入舊版交付包。

- `EnemyCombatPhase23.zip`
- `EnemyCombatPhase23-Fix1.zip`

移動前已搜尋資產 GUID，未找到其他 Assets 檔案引用。原始 ZIP 與 `.meta` 一併保留，未刪除內容。現行原始碼以 `Assets/Scripts/Enemy` 為準，勿直接覆蓋回舊版。
