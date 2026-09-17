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
        [Header("Provide a NetworkRunner prefab to be instantiated.\nIf no prefab is provided, a simple one will be created.")]
        [SerializeField] private NetworkRunner networkRunnerPrefab;

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
                SaveRepository);
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
