using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>跨 Domain Reload 保存這次明確啟動的測試結果；不修改 Scene。</summary>
[InitializeOnLoad]
public static class GrappleFeedbackTestResults
{
    private const string Key = "Purgers.GrappleFeedback.Results";
    private static TestRunnerApi api;
    static GrappleFeedbackTestResults() { if (SessionState.GetString(Key, "") != "") Attach(); }
    public static void Record(string name) { SessionState.SetString(Key, name); Attach(); }
    private static void Attach()
    {
        if (api != null) return;
        api = ScriptableObject.CreateInstance<TestRunnerApi>(); api.RegisterCallbacks(new Recorder());
    }
    private sealed class Recorder : ICallbacks
    {
        public void RunStarted(ITestAdaptor tests) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            string name = SessionState.GetString(Key, ""); if (name == "") return;
            Directory.CreateDirectory("Temp/GrappleFeedback");
            File.WriteAllText("Temp/GrappleFeedback/" + name + ".xml", result.ToXml().OuterXml);
            File.WriteAllText("Temp/GrappleFeedback/" + name + ".txt", "passed=" + result.PassCount +
                " failed=" + result.FailCount + " skipped=" + result.SkipCount + " result=" + result.ResultState);
            SessionState.SetString(Key, "");
        }
    }
}
