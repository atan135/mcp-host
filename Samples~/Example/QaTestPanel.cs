using System;
using System.Collections;
using QaTestFramework;
using UnityEngine;

namespace QaTestFramework.Example
{
    public sealed class QaTestPanel : MonoBehaviour
    {
        [QaTest("面板是否存在", "输出面板存在性检查的触发参数和模拟结果。", true)]
        private static bool PanelExists(string objectName)
        {
            QaTestLog.LogInput("QaTestPanel", "面板是否存在", "objectName=" + objectName);
            bool result = true;
            QaTestLog.LogResult("QaTestPanel", "面板是否存在", result.ToString());
            return result;
        }

        [QaTest("设置面板显隐", "输出设置面板显隐的触发参数和模拟结果。")]
        private static string SetPanelActive(string objectName, bool active = true)
        {
            QaTestLog.LogInput("QaTestPanel", "设置面板显隐", "objectName=" + objectName + ", active=" + active);
            string result = objectName + " active=" + active + " (mock)";
            QaTestLog.LogResult("QaTestPanel", "设置面板显隐", result);
            return result;
        }

        [QaTest("获取面板显隐状态", "输出获取面板显隐状态的触发参数和模拟结果。", true)]
        private static string GetPanelActiveState(string objectName)
        {
            QaTestLog.LogInput("QaTestPanel", "获取面板显隐状态", "objectName=" + objectName);
            string result = objectName + " activeSelf=true, activeInHierarchy=true (mock)";
            QaTestLog.LogResult("QaTestPanel", "获取面板显隐状态", result);
            return result;
        }

        [QaTest("等待面板显隐状态", "输出等待面板显隐状态的触发参数和模拟结果。", true)]
        private static QaTestCoroutineResult WaitPanelActive(string objectName, bool expectedActive = true, float timeoutSeconds = 3f)
        {
            QaTestLog.LogInput(
                "QaTestPanel",
                "等待面板显隐状态",
                "objectName=" + objectName + ", expectedActive=" + expectedActive + ", timeoutSeconds=" + timeoutSeconds);
            string result = string.Empty;
            return new QaTestCoroutineResult(
                WaitPanelActiveRoutine(objectName, expectedActive, timeoutSeconds, value => result = value),
                () => result);
        }

        private static IEnumerator WaitPanelActiveRoutine(string objectName, bool expectedActive, float timeoutSeconds, Action<string> complete)
        {
            yield return null;
            string result = objectName + " activeInHierarchy=" + expectedActive + ", timeoutSeconds=" + timeoutSeconds + " (mock)";
            QaTestLog.LogResult("QaTestPanel", "等待面板显隐状态", result);
            complete(result);
        }
    }
}
