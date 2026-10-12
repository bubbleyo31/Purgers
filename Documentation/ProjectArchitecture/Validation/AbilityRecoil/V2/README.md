# E 後座力 V2 驗證

日期：2026-10-08。範圍：槍械規則共用、鏡頭分段施加／回正、獨立複選時機與手動壓槍分配。配置見 [45 手冊](../../../45_E技能後座力配置.md)。[V1 歷史](../README.md) 保留當時結果。

## 自動化證據

- Red：Job 413feb7a3bea474f945835c7e5b6b195，20 項執行，新增 11 項按預期失敗（複選 4、尚缺回正 helper 7），原 9 項通過；見 [expected-failure.json](expected-failure.json)。
- Focused：Job 800da01763654edd944fbab4cf5e5cda，AbilityRecoilTests 13、RecoilRecoveryTests 7、WeaponRecoilApplicationTests 3，合計 23/23 通過；見 [focused.json](focused.json)。
- Full：Job b08b86b4cdff48769280eb1b7bfbaa91，PurgersRegression 探索 577／完成 562，狀態 failed、result=null；回報 BossStageAssetTests.GameSceneContainsBossAssetWiring 引用失敗（MapChunk_Boss 同名但不同引用）。相同既有問題見 [V1](../README.md) 及 ActiveAbilityStabilization，本次未修無關資產。不推算其餘 561 項皆通過，見 [regression.json](regression.json)。
- [Single Runner](single-runner.txt)：38 項 PASS。真實 Fusion 固定 Tick：合法事件／拒絕／取消、角度取樣、兩個時機各一次、無前搖特例一次、分段總角度、延遲、回正速率與終點、Yaw 交替且不回正、KCC 限制、Rifle 與技能共用真正手動輸入量、系統回正不互相吞掉。
- [Host／Client](host-client.txt)：同一 Editor 程序雙 Runner 的 17 項 PASS。Host 施放兩次事件、Client 實際 Ability1 輸入由 State Authority 執行、Proxy 禁止追加、長按不重複、權威位移及回正同步、晚加入與重連不重播舊事件。
- 結束後 Unity compileFailed=False、Console error/warning=0、Play Mode=False、目前場景 dirty=False。Editor 唯讀讀取確認十二項 E 的 cameraEnabled／movementEnabled=False、cameraTiming／movementTiming=0；新鏡頭時間／延遲／速度預設 0.06／0.1／20。
- 本輪前後 AbilityRuntime 及 ProfessionRuntime Prefab 雜湊比較見 [prefab-unchanged.json](prefab-unchanged.json)。沒有保存 Scene、Prefab 或 Definition。

## 邊界

Single Runner 只在暫時實例注入設定；Support 使用成功入口，Tank 的自然施放條件未全測。Host／Client 是受控同程序，並非獨立雙程序／雙機或 Build，未涵蓋人工視覺手感、延遲／丟包、每個技能全部自然操作及兩把槍的多人連射交互驗收。不能用本紀錄宣稱上述項目完成。

## 重跑

1. Unity MCP run_tests，mode=EditMode，test_names=[AbilityRecoilTests, RecoilRecoveryTests, WeaponRecoilApplicationTests]。
2. run_tests，mode=EditMode，category_names=[PurgersRegression]；確認非零實跑並讀取失敗明細。
3. 在 Play Mode 且無其他 Runner 時，execute_code 執行 AbilityRecoilRuntimeVerification.Begin()；讀取 Status 與 Temp/AbilityRecoilRuntimeVerification.txt，等測試完成。
4. 前項 Runner 已 Shutdown 後，execute_code 執行 AbilityRecoilNetworkVerification.Begin()；讀取 Status、Stage 與 Temp/AbilityRecoilNetworkVerification.txt。此測試需要 Photon 網路連線，使用不公開的隨機測試房間。
5. 兩項工具均只建立暫時物件／修改 Runtime 實例，finally 關閉 Runner；完成後退出 Play Mode，不保存測試設定。
