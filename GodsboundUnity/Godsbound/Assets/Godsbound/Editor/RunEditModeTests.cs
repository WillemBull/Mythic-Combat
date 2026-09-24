using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Godsbound.EditorTools
{
    /// <summary>
    /// Runs the full EditMode suite and writes NUnit XML to Logs/TestResults_latest.xml, plus a one-line
    /// summary to Logs/TestResults_latest.txt. Exists so a session can run the suite from the menu and
    /// read the result from disk instead of driving the Test Runner window.
    /// </summary>
    public static class RunEditModeTests
    {
        private static readonly string Dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs");

        [MenuItem("Godsbound/Run EditMode Tests")]
        public static void Run()
        {
            File.WriteAllText(Path.Combine(Dir, "TestResults_latest.txt"), "running " + DateTime.UtcNow.ToString("o") + Environment.NewLine);
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Writer());
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
        }

        private sealed class Writer : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                File.WriteAllText(Path.Combine(Dir, "TestResults_latest.xml"), result.ToXml().OuterXml);
                File.WriteAllText(Path.Combine(Dir, "TestResults_latest.txt"),
                    $"done {DateTime.UtcNow:o} total={result.PassCount + result.FailCount + result.SkipCount + result.InconclusiveCount} " +
                    $"passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount} duration={result.Duration:0.###}" + Environment.NewLine);
            }
        }
    }
}
