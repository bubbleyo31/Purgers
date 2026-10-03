# 變異荊棘 V1

三款深褐色密刺球塊，為後續飛行障礙物提供美術模型。完整設定與驗證見 [161 說明書](../../../Documentation/ProjectArchitecture/161_變異荊棘模型與視覺動畫.md)。

- [三款實際模型預覽](Previews/Variants.png)：左 A 緊密刺球、中 B 扭結團塊、右 C 裂核刺冠；這是 Blender 網格渲染。
- [Blender 原檔](MutantThorns_V1.blend)：含三款模型、三級 LOD 與已打包貼圖；沒有骨骼或動畫 clip。
- [Unity 模型與 Prefab](../../../Assets/_Project_Assets/Models/Map/MutantThorns_V1)：九個 FBX、共用 URP 材質、三個美術 Prefab。
- [Unity 套件](MutantThorns_V1.unitypackage)：模型、材質、Prefab 與動畫腳本；適用已安裝 URP 的 Unity 專案，不含 URP 套件本身。
- [Unity A 實際預覽](Previews/Unity_A_BriarHeart.png)、[B](Previews/Unity_B_GnarledKnot.png)、[C](Previews/Unity_C_SplitCrown.png)：預覽照明與正式場景不同。
- [面數統計](mesh_report.json)、[FBX 往返檢查](fbx_validation.json)、[Unity 資產檢查](unity_asset_validation.json)、[Unity 實際尺寸](unity_world_dimensions.json)、[最終匯入警告檢查](final_import_logs.json)。

動畫採用 Unity `MutantThornVisual`：預設 Idle 呼吸週期 3.125 秒、受傷微抽動 0.28 秒。受傷入口是 `PlayHitReaction(1f)`，尚未接遊戲傷害事件。單獨把 FBX 匯入其他引擎不會帶有這段程式動畫。

未配置 Collider、生成器或網路傷害，也未放入遊戲場景。請依說明書手動驗收外觀與動畫，再決定關卡整合。荊棘測試 5/5；完整回歸執行 205 項，另有 5 項未修改模組的測試失敗，詳見說明書。
