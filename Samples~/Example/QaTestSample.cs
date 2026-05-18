using System.Collections;
using QaTestFramework;
using UnityEngine;

namespace QaTestFramework.Example
{
    public sealed class QaTestSample : MonoBehaviour
    {
        [QaTest("实例连通性检查", "验证 MonoBehaviour 实例方法可以被注册。", true)]
        private string PingFromInstance()
        {
            QaTestLog.LogInput("QaTestSample", "实例连通性检查", "gameObject=" + gameObject.name);
            string result = "pong from instance: " + gameObject.name;
            QaTestLog.LogResult("QaTestSample", "实例连通性检查", result);
            return result;
        }

        [QaTest("连通性检查", "从 Unity 返回一个简单响应。", true)]
        private static string Ping()
        {
            QaTestLog.LogInput("QaTestSample", "连通性检查", "无");
            string result = "pong";
            QaTestLog.LogResult("QaTestSample", "连通性检查", result);
            return result;
        }

        [QaTest("输出日志", "向 Unity 控制台写入一条消息。")]
        private static string LogMessage(string message = "hello from qa")
        {
            QaTestLog.LogInput("QaTestSample", "输出日志", "message=" + message);
            string result = "logged: " + message;
            QaTestLog.LogResult("QaTestSample", "输出日志", result);
            return result;
        }

        [QaTest("等待后返回", "异步等待并返回协程结果。")]
        private static IEnumerator WaitAndReturn(float seconds = 1f, string message = "coroutine completed")
        {
            QaTestLog.LogInput("QaTestSample", "等待后返回", "seconds=" + seconds + ", message=" + message);
            yield return new WaitForSeconds(seconds);
            QaTestLog.LogResult("QaTestSample", "等待后返回", message);
            yield return QaTestCoroutineReturn.From(message);
        }
    }
}
