using UnityEngine;

namespace QaTestFramework
{
    public static class QaTestLog
    {
        public static bool Enabled { get; set; } = true;

        public static void LogInput(string source, string actionName, string parameters)
        {
            if (!Enabled)
            {
                return;
            }

            Debug.Log("[" + source + "] 触发: " + actionName + "；参数: " + parameters);
        }

        public static void LogResult(string source, string actionName, string result)
        {
            if (!Enabled)
            {
                return;
            }

            Debug.Log("[" + source + "] 返回: " + actionName + "；结果: " + result);
        }
    }
}
