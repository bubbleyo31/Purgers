using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Purgers.GameFlow.Stage
{
    [DisallowMultipleComponent]
    public sealed class StageHudController : MonoBehaviour
    {
        [SerializeField] private StageFlowController flowController;
        [SerializeField] private TMP_Text stageLevelLabel;
        [SerializeField] private TMP_Text timerLabel;
        [SerializeField] private TMP_Text objectiveLabel;
        [SerializeField] private TMP_Text extractionStatusLabel;
        [SerializeField] private Slider extractionProgress;
        [SerializeField] private Color timerNormalColor = Color.white;
        [SerializeField] private Color timerWarningColor = new Color(1f, 0.24f, 0.25f, 1f);

        private NetworkRunner hudRunner;
        private int hudStageLevel;
        private int hudSceneHandle;
        private bool hasHudClaim;

        public NetworkRunner HudRunner => hasHudClaim ? hudRunner : null;

        private void Awake()
        {
            hudSceneHandle = gameObject.scene.handle;
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            if (extractionProgress)
            {
                extractionProgress.minValue = 0f;
                extractionProgress.maxValue = 1f;
                extractionProgress.value = 0f;
            }
        }

        private void Update()
        {
            if (!TryMaintainStageOwnership())
                return;

            if (stageLevelLabel)
            {
                stageLevelLabel.text =
                    StageHudText.BuildStageHeader(
                        flowController.StageLevel,
                        flowController.CycleStage,
                        flowController.CycleLength,
                        flowController.IsBossStage);
            }

            if (timerLabel)
            {
                timerLabel.text = flowController.Phase switch
                {
                    StagePhase.Active when flowController.HasTimeLimit =>
                        FormatTime(flowController.RemainingStageSeconds),
                    StagePhase.Active => "不限時",
                    _ => string.Empty
                };
                timerLabel.color = flowController.Phase == StagePhase.Active &&
                    flowController.HasTimeLimit &&
                    ShouldWarnAboutTime(flowController.RemainingStageSeconds)
                    ? timerWarningColor : timerNormalColor;
            }

            if (objectiveLabel)
            {
                objectiveLabel.text = flowController.Phase switch
                {
                    StagePhase.Initializing => "正在準備關卡…",
                    StagePhase.Active => flowController.ObjectiveKind ==
                            StageObjectiveKind.DefeatBoss
                        ? "擊敗 Boss"
                        : flowController.ExtractionHoldProgress > 0f
                            ? $"全員撤離中  {flowController.RemainingExtractionSeconds:F1}s"
                            : flowController.IsLocalPlayerInsideExtraction()
                                ? "等待其他存活玩家進入撤離區"
                                : "全體存活玩家抵達撤離點",
                    StagePhase.Completed => flowController.ObjectiveKind ==
                            StageObjectiveKind.DefeatBoss
                        ? "Boss 已擊敗"
                        : "撤離成功",
                    StagePhase.Failed => "關卡失敗",
                    StagePhase.CommitFailed => "存檔寫入失敗，請查看 Console",
                    StagePhase.LoadingSafeHouse => "正在返回安全屋…",
                    _ => string.Empty
                };
            }

            float progress = flowController.RequiresExtraction
                ? flowController.ExtractionHoldProgress
                : 0f;
            if (extractionProgress)
                extractionProgress.value = progress;

            if (extractionStatusLabel)
            {
                if (flowController.Phase != StagePhase.Active ||
                    !flowController.RequiresExtraction)
                {
                    extractionStatusLabel.text = string.Empty;
                }
                else if (progress > 0f)
                {
                    extractionStatusLabel.text =
                        $"撤離倒數  {flowController.RemainingExtractionSeconds:F1}s";
                }
                else
                {
                    extractionStatusLabel.text =
                        flowController.IsLocalPlayerInsideExtraction()
                            ? "等待其他存活玩家進入撤離區"
                            : "前往綠色撤離區";
                }
            }
        }

        private bool TryMaintainStageOwnership()
        {
            if (!flowController || !flowController.IsNetworkReady || !flowController.Runner)
            {
                if (hasHudClaim)
                    ClearAndDestroy();
                else
                    ClearPresentation();

                return false;
            }

            NetworkRunner currentRunner = flowController.Runner;
            int currentStageLevel = flowController.StageLevel;
            if (!hasHudClaim)
            {
                hudRunner = currentRunner;
                hudStageLevel = currentStageLevel;
                hasHudClaim = true;
                StageHudLifetimeRegistry.Claim(hudRunner, hudStageLevel, this);
            }
            else if (hudRunner != currentRunner || hudStageLevel != currentStageLevel)
            {
                ClearAndDestroy();
                return false;
            }

            return StageHudLifetimeRegistry.IsOwner(
                hudRunner,
                hudStageLevel,
                this);
        }

        private void OnSceneUnloaded(Scene scene)
        {
            // Fusion can migrate this HUD before unloading its original scene.
            if (scene.handle == hudSceneHandle && gameObject.scene.handle == scene.handle)
                ClearAndDestroy();
        }

        private void OnDisable()
        {
            ClearPresentation();
        }

        private void OnDestroy()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            StageHudLifetimeRegistry.Release(hudRunner, this);
            ClearPresentation();
        }

        private void ClearAndDestroy()
        {
            ClearPresentation();
            StageHudLifetimeRegistry.Release(hudRunner, this);
            hasHudClaim = false;

            if (gameObject.activeSelf)
                gameObject.SetActive(false);

            if (Application.isPlaying)
                Destroy(gameObject);
            else
                DestroyImmediate(gameObject);
        }

        private void ClearPresentation()
        {
            if (stageLevelLabel)
                stageLevelLabel.text = string.Empty;
            if (timerLabel)
                timerLabel.text = string.Empty;
            if (timerLabel)
                timerLabel.color = timerNormalColor;
            if (objectiveLabel)
                objectiveLabel.text = string.Empty;
            if (extractionStatusLabel)
                extractionStatusLabel.text = string.Empty;
            if (extractionProgress)
                extractionProgress.value = 0f;
        }

        private static string FormatTime(float seconds)
        {
            int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));
            int minutes = totalSeconds / 60;
            int remainder = totalSeconds % 60;
            return $"{minutes:00}:{remainder:00}";
        }

        private static bool ShouldWarnAboutTime(float seconds) => seconds < 60f;
    }
}
