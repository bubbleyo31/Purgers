> **後續修訂：速度動能增益**：依使用者指示改名，只有量條滿格才顯示，未滿不再按比例出現；離開滿格後沿原時間淡出。以下保留首次整合證據，最新門檻驗證見 [FullOnly](FullOnly/README.md)。

# 治療／受傷／橘金外框正式整合驗證

最後核對日期：2026-10-19（局部）。正式責任、設定及操作入口見 [112 模組](../../112_玩家畫面外框狀態效果.md)。

## 實作與資產

- 已新增 Runtime Presenter、可調 Settings、UGUI Graphic、事件刷新包絡及共用程序 Shader；正式 Prefab／_Menu 已接線。
- 維持 PlayerHealth 實際結果事件、Input Authority 收件與既有動能 Owner；未新增 Networked 欄位或 RPC。
- Canvas -5，位於護目鏡 -10 上方與 HUD 0／100 下方；不攔輸入。最終外框 sibling 7、護目鏡原 sibling 0 保留。
- 初次保存前比較 1794 個既有元件序列化快照，見 [wiring.txt](wiring.txt)。工作樹原本就有其他功能的未提交變動，不能把整份 Scene diff 歸因於本輪。
- 完整回歸曾捕捉到插入 FirstSibling 導致護目鏡原排序測試失敗；已改為末尾附加並修正正式場景，既有測試未放寬。
- [最終 Editor 審核](final-editor-audit.json)：EditMode、沒有編譯進行中、Scene 無未儲存變更、外框 1 組、設定有效、Missing Script 0、Shader error false、測試 Runner 0。materialSerialized=true 表示取回的是 Graphic 預設材質，沒有把 HideAndDontSave 執行期材質寫入 Prefab。

## 自動化測試

| 階段 | 結果 | 證據 |
|---|---|---|
| 先失敗 | 尚未有 Runtime Presenter，指定測試預期失敗 | [red.xml](red.xml) |
| 最終聚焦 | 56 / 56 通過 | [XML](focused.xml)、[RunFinished](focused-summary.txt) |
| 完整 PurgersRegression | 1243：1237 通過、4 失敗、2 跳過，**不是全綠** | [XML](regression.xml)、[RunFinished](regression-summary.txt) |

聚焦包含 ScreenEdgeEffects、AbilityColorGrading、DamageIndicator、FirstPersonGoggles、FirstPersonGogglesWiring、HudLayout。新增外框測試共 8 項，涵蓋實際 Runtime 類型、無效傷害／回血輸入、連續刷新不跳亮／不疊加、停止清除、混合優先權、正式 Shader／Prefab 與不攔 Raycast。

與 [整合前基線](../HealingEdgePreview/v1.2/regression.xml) 比對，4 個失敗名稱完全相同：

1. BossStageAssetTests.GameSceneContainsBossAssetWiring
2. EnemyFlytrapAssetTests.TimingDefaultsMatchApprovedRules
3. EnemyReflectorAssetTests.SeparatePrefabUsesExistingCombatAndHealthOwners
4. ReloadCameraRuntimeTests.SpawnedLocalWeaponsDriveOnlyPresentationAndUnbindClearsIt

MCP job 在 Domain Reload 後保留舊 running 狀態；以上統計採當次新的 RunFinished XML（07:13:26 UTC），確認完成後才清除失效 job，沒有把 total=0 或過期輪詢當成功。

## 同 Editor 受控 Host／Client

實際 Photon Fusion Host／Client、真實 KCC_Player、原 DamageReceiverUtility／RestoreHealth 與正式外框 Presenter。工具最終觀察 Status=通過、Stage=disconnect clears，並於 finally 清除 Runner／暫存物件、恢復正式 HUD 自動解析。

檢查：Host 單次傷害／治療、Client 受擊與連續回血／受傷、其他玩家無事件洩漏、Client 無權修改血量、完全格擋無紅外框、事件停止淡出、晚加入不重播、動能資料只到自己的外框、停用釋放舊 LGG 治療所有權、重新啟用恢復動能、動能歸零、死亡清除、Despawn／新玩家重綁與斷線清除。

[host-client.txt](host-client.txt) 記錄顯式 Check 的 29 筆 PASS。Until 等待條件成功不逐筆寫入該檔案，完成狀態另記 [觀察紀錄](host-client-status.json)，不可把該文字檔當作逐步完整測試 XML。

動能分支使用 Host 測試注入後經真正複寫讀取，驗證資料與呈現歸屬；不代表自然鈎索移動充能或世界速度驗收。測試同 Editor 多 Peer，不等於獨立 Player／多機。

## 真實 HUD 離屏渲染與預覽外觀保留

使用正式 Prefab／UGUI Shader 與複製的實際 HUD，以相機離屏渲染 1920×1080；未使用歷史截圖／色彩遮罩，也未聚焦 Scene View。平面底色用於像素比較，不是自然實戰截圖。

- [無效果](before.png)、[治療](healing.png)、[受傷](damage.png)、[橘金](momentum.png)、[三者同時](combined.png)。
- 中央測區 RGB 最大差異 0；169544 個 HUD 常見實色樣本最大差異 1/255。這是明確保留並記錄的容差，不能宣稱 HUD 每個像素都完全一致。半透明 HUD 仍會正常透出後方效果。
- Editor 預覽提取共用 Shader 後，三組歷史峰值的最大像素差異均為 0；對照圖為 healing-parity／damage-parity／momentum-parity.png。
- 數據見 [pixel-verification.json](pixel-verification.json)；可重跑 `deliverables/ScreenEdgeIntegration_20261019/verify_render.py`。

## 未驗收範圍

自然遊戲中的主觀強度／節奏、各種地圖亮度、所有寬高比、完整換關／重新連線流程、獨立 Player、多機網路、Build 與目標平台 GPU 效能仍待驗。受控換人／斷線與靜態 HUD 渲染不能替代上述驗收。本輪未新增殘血效果。
