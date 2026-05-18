using System.Collections;
using QaTestFramework;
using UnityEngine;

namespace QaTestFramework.Example
{
    public sealed class QaTestControl : MonoBehaviour
    {
        [QaTest("点击按钮控件", "输出按钮点击的触发参数和模拟结果。")]
        private static string ClickButton(string objectName)
        {
            QaTestLog.LogInput("QaTestControl", "点击按钮控件", "objectName=" + objectName);
            string result = "clicked: " + objectName + " (mock)";
            QaTestLog.LogResult("QaTestControl", "点击按钮控件", result);
            return result;
        }

        [QaTest("设置控件可交互状态", "输出设置控件可交互状态的触发参数和模拟结果。")]
        private static string SetInteractable(string objectName, bool interactable = true)
        {
            QaTestLog.LogInput("QaTestControl", "设置控件可交互状态", "objectName=" + objectName + ", interactable=" + interactable);
            string result = objectName + " interactable=" + interactable + " (mock)";
            QaTestLog.LogResult("QaTestControl", "设置控件可交互状态", result);
            return result;
        }

        [QaTest("检查控件是否可交互", "输出控件可交互检查的触发参数和模拟结果。", true)]
        private static bool IsInteractable(string objectName)
        {
            QaTestLog.LogInput("QaTestControl", "检查控件是否可交互", "objectName=" + objectName);
            bool result = true;
            QaTestLog.LogResult("QaTestControl", "检查控件是否可交互", result.ToString());
            return result;
        }

        [QaTest("等待控件可交互状态", "输出等待控件可交互状态的触发参数和模拟结果。", true)]
        private static IEnumerator WaitInteractable(string objectName, bool expectedInteractable = true, float timeoutSeconds = 3f)
        {
            QaTestLog.LogInput(
                "QaTestControl",
                "等待控件可交互状态",
                "objectName=" + objectName + ", expectedInteractable=" + expectedInteractable + ", timeoutSeconds=" + timeoutSeconds);
            yield return null;
            string result = objectName + " interactable=" + expectedInteractable + ", timeoutSeconds=" + timeoutSeconds + " (mock)";
            QaTestLog.LogResult("QaTestControl", "等待控件可交互状态", result);
            yield return QaTestCoroutineReturn.From(result);
        }
    }
}
