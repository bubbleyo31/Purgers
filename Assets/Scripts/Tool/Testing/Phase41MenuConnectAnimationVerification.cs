#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Reflection;
using Fusion;
using Fusion.Menu;
using MultiClimb.Menu;
using Purgers.GameFlow.Control;
using Purgers.GameFlow.Transition;
using Purgers.Progression;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Opt-in probe for the real Fusion Menu connection animation path.</summary>
[DefaultExecutionOrder(-180)]
public sealed class Phase41MenuConnectAnimationVerification : MonoBehaviour
{
    private const BindingFlags PrivateInstance =
        BindingFlags.Instance | BindingFlags.NonPublic;

    private string outputPath;
    private FusionMenuUILoading loadingScreen;
    private NetworkRunner runner;
    private float startedAt;
    private float loadingVisibleAt = -1f;
    private float loadingHiddenAt = -1f;
    private float nextLogAt;
    private bool sawLoadingVisible;
    private bool sawTransitionWhileLoadingVisible;
    private bool sawNonTransparentFadeWhileLoadingVisible;
    private bool sawLoadingHiddenAfterVisible;
    private bool sawSafeHouseLoaded;
    private bool completed;

    public static void StartVerification(string outputDirectory)
    {
        if (!Application.isPlaying ||
            FindObjectOfType<Phase41MenuConnectAnimationVerification>() != null)
        {
            return;
        }

        MenuConnectionBehaviour connection =
            FindObjectOfType<MenuConnectionBehaviour>();
        FusionMenuUIMain main = FindObjectOfType<FusionMenuUIMain>();
        MenuUIController controller = FindObjectOfType<MenuUIController>();
        if (connection == null || main == null || controller == null)
            throw new InvalidOperationException("Fusion Menu objects are missing.");

        string output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        var repository = new JsonGameSaveRepository(
            Path.Combine(output, "Saves"));
        typeof(MenuConnectionBehaviour)
            .GetField("saveRepository", PrivateInstance)
            .SetValue(connection, repository);
        connection.Connection = null;

        var probe = new GameObject("Phase41MenuConnectAnimationVerification")
            .AddComponent<Phase41MenuConnectAnimationVerification>();
        DontDestroyOnLoad(probe.gameObject);
        probe.outputPath = Path.Combine(output, "connect-animation.log");
        probe.loadingScreen = controller.Get<FusionMenuUILoading>();
        probe.startedAt = Time.unscaledTime;
        probe.Log("BEGIN actual Quick Play through FusionMenuUIMain");

        typeof(FusionMenuUIMain)
            .GetMethod("OnPlayButtonPressed", PrivateInstance)
            .Invoke(main, null);
    }

    private void Update()
    {
        if (completed)
            return;

        if (Time.unscaledTime - startedAt > 120f)
        {
            Finish(false, "timeout");
            return;
        }

        bool loadingVisible = loadingScreen != null &&
                              loadingScreen.IsShowing &&
                              loadingScreen.gameObject.activeInHierarchy;
        if (loadingVisible)
        {
            sawLoadingVisible = true;
            if (loadingVisibleAt < 0f)
                loadingVisibleAt = Time.unscaledTime;
        }
        else if (sawLoadingVisible)
        {
            sawLoadingHiddenAfterVisible = true;
            if (loadingHiddenAt < 0f)
                loadingHiddenAt = Time.unscaledTime;
        }

        if (runner == null)
        {
            foreach (NetworkRunner candidate in FindObjectsOfType<NetworkRunner>())
            {
                if (candidate != null && candidate.IsRunning)
                {
                    runner = candidate;
                    break;
                }
            }
        }

        LocalSceneTransition transition =
            runner == null ? null : runner.GetComponent<LocalSceneTransition>();
        if (transition != null && transition.IsTransitioning && loadingVisible)
        {
            sawTransitionWhileLoadingVisible = true;
            if (transition.Fade == null || transition.Fade.Alpha > 0.01f)
                sawNonTransparentFadeWhileLoadingVisible = true;
        }

        if (Time.unscaledTime >= nextLogAt)
        {
            nextLogAt = Time.unscaledTime + 0.25f;
            Log(
                "FRAME loading=" + loadingVisible +
                " showing=" + (loadingScreen != null && loadingScreen.IsShowing) +
                " active=" +
                (loadingScreen != null && loadingScreen.gameObject.activeInHierarchy) +
                " gate=" + MenuConnectionUiTransitionGate.IsConnectUiVisible +
                " transition=" + (transition != null && transition.IsTransitioning) +
                " fade=" +
                (transition != null && transition.Fade != null
                    ? transition.Fade.Alpha.ToString("F2")
                    : "none") +
                " scene=" + SceneManager.GetActiveScene().name +
                " locks=" + LocalPlayerControl.Locks.Mask);
        }

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).name == "SafeHouse")
            {
                sawSafeHouseLoaded = true;
                break;
            }
        }

        if (sawSafeHouseLoaded && sawLoadingHiddenAfterVisible &&
            transition != null && !transition.IsTransitioning)
        {
            bool visibleLongEnough = loadingVisibleAt >= 0f &&
                loadingHiddenAt >= loadingVisibleAt &&
                loadingHiddenAt - loadingVisibleAt >= 0.25f;
            bool passed = sawLoadingVisible &&
                          visibleLongEnough &&
                          sawTransitionWhileLoadingVisible &&
                          !sawNonTransparentFadeWhileLoadingVisible &&
                          sawLoadingHiddenAfterVisible &&
                          sawSafeHouseLoaded;
            Finish(
                passed,
                "visible=" + sawLoadingVisible +
                ", visibleLongEnough=" + visibleLongEnough +
                ", transitionWhileVisible=" + sawTransitionWhileLoadingVisible +
                ", transparentForEntireVisibleTransition=" +
                !sawNonTransparentFadeWhileLoadingVisible +
                ", hiddenAfter=" + sawLoadingHiddenAfterVisible +
                ", safeHouseLoaded=" + sawSafeHouseLoaded);
        }
    }

    private void Finish(bool passed, string details)
    {
        Log((passed ? "PASS " : "FAIL ") + details);
        completed = true;
    }

    private void Log(string message)
    {
        string line = DateTime.UtcNow.ToString("O") + " " + message;
        File.AppendAllText(outputPath, line + Environment.NewLine);
        Debug.Log("[Phase41MenuConnectAnimation] " + message);
    }
}
#endif
