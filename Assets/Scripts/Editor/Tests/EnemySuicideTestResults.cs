using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>只在明確 Arm 後收集一次測試結果，跨 EditMode 測試的 Domain Reload 保存證據。</summary>
[InitializeOnLoad]
public sealed class EnemySuicideTestResults : ICallbacks
{
    private const string ActiveKey = "Purgers.SuicideTests.ResultPath";
    static EnemySuicideTestResults() { if (!string.IsNullOrEmpty(SessionState.GetString(ActiveKey,""))) Register(); }
    private static void Register() => ScriptableObject.CreateInstance<TestRunnerApi>().RegisterCallbacks(new EnemySuicideTestResults());
    public static void Arm(string path) { SessionState.SetString(ActiveKey,path); Register(); }
    public void RunStarted(ITestAdaptor tests) { }
    public void TestStarted(ITestAdaptor test) { }
    public void TestFinished(ITestResultAdaptor result) { }
    public void RunFinished(ITestResultAdaptor result)
    {
        string path=SessionState.GetString(ActiveKey,"");
        if(string.IsNullOrEmpty(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var lines=new List<string>(); Append(result,lines); File.WriteAllLines(path,lines);
        SessionState.EraseString(ActiveKey);
    }
    private static void Append(ITestResultAdaptor result,List<string> lines)
    {
        lines.Add(result.FullName+" | "+result.ResultState+" | passed="+result.PassCount+" failed="+result.FailCount+" skipped="+result.SkipCount+" | "+result.Message+" | "+result.StackTrace);
        foreach(var child in result.Children) Append(child,lines);
    }
}
