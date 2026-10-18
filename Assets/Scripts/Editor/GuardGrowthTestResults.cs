using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>明確指定輸出名才保存 RunFinished，跨 Domain Reload，無視窗／焦點操作。</summary>
[InitializeOnLoad]
public sealed class GuardGrowthTestResults : ICallbacks
{
    private const string Key = "Purgers.GuardGrowth.ResultName";
    private static readonly TestRunnerApi Api;
    static GuardGrowthTestResults()
    {
        Api=ScriptableObject.CreateInstance<TestRunnerApi>();
        Api.hideFlags=HideFlags.HideAndDontSave;
        Api.RegisterCallbacks(new GuardGrowthTestResults());
    }
    public static void Arm(string name) => SessionState.SetString(Key,name);
    public void RunStarted(ITestAdaptor tests) { }
    public void TestStarted(ITestAdaptor test) { }
    public void TestFinished(ITestResultAdaptor result) { }
    public void RunFinished(ITestResultAdaptor result)
    {
        string name=SessionState.GetString(Key,""); if(string.IsNullOrEmpty(name))return;
        SessionState.SetString(Key,"");
        Directory.CreateDirectory("Temp/GuardGrowthVerification");
        File.WriteAllText("Temp/GuardGrowthVerification/"+name+".xml",result.ToXml().OuterXml);
        File.WriteAllText("Temp/GuardGrowthVerification/"+name+".txt",DateTime.UtcNow.ToString("O")+"\n"+
            result.ResultState+" passed="+result.PassCount+" failed="+result.FailCount+" skipped="+result.SkipCount);
    }
}
