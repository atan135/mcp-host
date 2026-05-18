using QaTestFramework;

namespace QaTestFramework.Example
{
    public static class QaTestStaticSample
    {
        [QaTest("静态类连通性检查", "验证 static class 中的 QaTest 方法可以被注册。", true)]
        private static string PingFromStaticClass()
        {
            QaTestLog.LogInput("QaTestStaticSample", "静态类连通性检查", "无");
            string result = "pong from static class";
            QaTestLog.LogResult("QaTestStaticSample", "静态类连通性检查", result);
            return result;
        }
    }
}
