using UnityEngine.Scripting;

namespace QaTestFramework
{
    [Preserve]
    public static class QaTestSample
    {
        [Preserve]
        [QaTest("连通性检查", "Runtime ping-pong check for validating that the game can execute QA commands.")]
        private static string Ping()
        {
            QaTestLog.LogInput("QaTestSample", "连通性检查", "无");
            const string result = "pong";
            QaTestLog.LogResult("QaTestSample", "连通性检查", result);
            return result;
        }
    }
}
