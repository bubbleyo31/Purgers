# Assets/Scripts/Combat 程式導覽

傷害、生命、狀態與跨系統戰鬥回饋。

## 修改入口

- 先由下表依功能找對腳本，再讀取其 `Tooltip`、序列化欄位與公開方法。
- 涉及 Fusion 資料、場景轉場、生成或傷害時，先確認 State Authority／Scene Authority，Client 不可直接寫入結果。
- 新增功能應放進責任最接近的子資料夾；純判定規則保持無場景依賴並搭配 EditMode 測試。

## 直接腳本

這是分類節點，請繼續閱讀下層資料夾的 README。

## 快速查找

- Inspector 設定：搜尋 `[Header(`、`[Tooltip(` 或 `[SerializeField]`。
- 網路同步：搜尋 `[Networked]`、`[Rpc(`、`HasStateAuthority`、`IsSceneAuthority`。
- 場景綁定：由 `[SerializeField]` 欄位名稱回到 Unity Inspector 找同名引用。


## 雷射本機呈現（2026-10-18）

- BeamPresentationSettings：各 Prefab 獨立的粗細、顏色、粒子及四階段聲音；含純規則與事件去重。
- BeamPresentationRuntime／.Particles：共用線段、亮核、火花；沿 GameplayAudioService，不擁有傷害或權威階段。
- 完整設定與驗證：Documentation/ProjectArchitecture/108_雷射四階段視聽呈現.md。

