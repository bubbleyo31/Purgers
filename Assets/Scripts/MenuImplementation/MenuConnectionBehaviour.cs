using Fusion;
using Fusion.Menu;
using Purgers.Progression;
using UnityEngine;

namespace MultiClimb.Menu
{
    public partial class MenuConnectionBehaviour : FusionMenuConnectionBehaviour
    {
        [SerializeField] private FusionMenuConfig config;
        [Space]
        [Header("NetworkRunner 預置物：未指定時會建立簡易執行器。")]
        [SerializeField] private NetworkRunner networkRunnerPrefab;

        [Header("新存檔規則")]
        [Tooltip(
            "Quick Play 建立新存檔時固定寫入的 CycleLength。" +
            "既有 Continue 存檔保留自己的 Snapshot，不會被此值改寫。")]
        [SerializeField, Min(1)] private int newSaveCycleLength =
            GameSaveSchema.DefaultCycleLength;

        [Header("連線等待")]
        [Tooltip("選區、建立／加入房間及初次載入合計的等待上限（秒，最小 1，預設 30）。逾時後清理最多另等 5 秒，再回主選單重試；不影響已連線遊戲的斷線判定。")]
        [SerializeField, Min(1)] private float connectionTimeoutSeconds = 30f;

        private IGameSaveRepository saveRepository;

        public IGameSaveRepository SaveRepository =>
            saveRepository ?? (saveRepository = new JsonGameSaveRepository());

        private void Awake()
        {
            if (!config)
                Log.Error("Fusion menu configuration file not provided.");
        }

        public override IFusionMenuConnection Create()
        {
            var connection = new MenuConnection(
                config,
                networkRunnerPrefab,
                SaveRepository,
                newSaveCycleLength,
                connectionTimeoutSeconds);
            connection.RoomClosed += () => ShowRoomClosed(connection);
            connection.ConnectionAttemptFailed += ShowConnectionFailure;
            return connection;
        }

        private void ShowConnectionFailure(string message)
        {
            if (!this || !Application.isPlaying) return;
            var main = GetComponentInChildren<FusionMenuUIMain>(true);
            if (!main) main = transform.root.GetComponentInChildren<FusionMenuUIMain>(true);
            if (!main || main.Controller == null) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            main.Controller.Show<FusionMenuUIMain>();
            // 取消直接返回；逾時／失敗在主選單上沿用既有 Popup。
            if (!string.IsNullOrEmpty(message)) main.Controller.Popup(message, "連線未完成");
        }

        public bool TrySelectHostSave(
            GameSaveData save,
            out string error)
        {
            if (save == null)
            {
                error = "不能選擇空的存檔。";
                return false;
            }

            if (Connection == null)
                Connection = Create();

            if (Connection is MenuConnection menuConnection)
            {
                menuConnection.SelectHostSave(save);
                error = string.Empty;
                return true;
            }

            error = "目前 Fusion Menu Connection 不是專案的 MenuConnection。";
            return false;
        }

        public void ClearSelectedHostSave()
        {
            if (Connection is MenuConnection menuConnection)
                menuConnection.ClearSelectedHostSave();
        }
    }
}
