using QaTestFramework;

namespace QaTestFramework.Example
{
    public sealed class QaTestUtilitySample
    {
        [QaTest("普通类静态方法检查", "验证普通 class 中的静态 QaTest 方法可以被注册。", true)]
        private static string PingFromUtilityClass(string message = "utility")
        {
            QaTestLog.LogInput("QaTestUtilitySample", "普通类静态方法检查", "message=" + message);
            string result = "pong from utility class: " + message;
            QaTestLog.LogResult("QaTestUtilitySample", "普通类静态方法检查", result);
            return result;
        }
    }
}
