using Fusion;
using Fusion.Menu;
using Purgers.Progression;
using UnityEngine;

namespace MultiClimb.Menu
{
    public class MenuConnectionBehaviour : FusionMenuConnectionBehaviour
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
            return new MenuConnection(
                config,
                networkRunnerPrefab,
                SaveRepository,
                newSaveCycleLength);
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
