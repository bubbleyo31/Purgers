# Phase 4-D 獨立 Host／Client 驗證

- Build：`build-996a7ed53f`，Windows Development Player，0 errors／0 warnings。
- Host 與 Client 為兩個不同 `Purgers.exe` 程序；Client 以 20 秒延遲加入已進入關卡的 Host。
- `Host.snapshot` 是兩端共同比對的權威拓撲：3 Chunk、`FinalChunkIndex = 2`，包含每塊 Catalog／Cartography 版本、父連線、入口、出口、開口遮罩、Pose 與撤離索引／PointId。
- `Host.log` 驗證 Host 導航 Ready、三個 Patrol Area、完整封口與撤離。
- `Client.log` 驗證晚加入重建相同 Chunk／Collider／Cartography／封口／撤離，Client 導航未 Ready、0 Patrol Area、NavMeshSurface 全停用。
- `HostPlayer.log`、`ClientPlayer.log` 搜尋 `FAIL`、Exception、Null／Missing Reference、Assertion 與 Error 均無命中。

`Client.complete` 是測試探針完成標記，不是遊戲資料。
