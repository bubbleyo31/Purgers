using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>Explicitly armed result capture; survives test-triggered domain reload without focusing a window.</summary>
[InitializeOnLoad]
public sealed class TankIndicatorTestResults : ICallbacks
{
    const string Key = "Purgers.TankIndicator.CaptureTests";
    static readonly TestRunnerApi Api;
    static TankIndicatorTestResults()
    {
        Api = ScriptableObject.CreateInstance<TestRunnerApi>();
        Api.hideFlags = HideFlags.HideAndDontSave;
        Api.RegisterCallbacks(new TankIndicatorTestResults());
    }
    [MenuItem("Tools/Purgers/Verification/Tank Indicator/Capture Next Test Result")]
    static void Arm() => SessionState.SetBool(Key, true);
    public void RunStarted(ITestAdaptor tests) { }
    public void TestStarted(ITestAdaptor test) { }
    public void TestFinished(ITestResultAdaptor result) { }
    public void RunFinished(ITestResultAdaptor result)
    {
        if (!SessionState.GetBool(Key, false)) return;
        SessionState.SetBool(Key, false);
        Directory.CreateDirectory("Temp/TankIndicator");
        File.WriteAllText("Temp/TankIndicator/regression.xml", result.ToXml().OuterXml);
        File.WriteAllText("Temp/TankIndicator/regression-summary.txt", DateTime.UtcNow.ToString("O") + "\n" +
            result.ResultState + " passed=" + result.PassCount + " failed=" + result.FailCount +
            " skipped=" + result.SkipCount + " inconclusive=" + result.InconclusiveCount);
    }
}
