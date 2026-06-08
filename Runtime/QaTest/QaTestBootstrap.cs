using UnityEngine;

namespace QaTestFramework
{
    public static class QaTestBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateClient()
        {
            if (!Application.isEditor && QaTestClient.GetAutoConnectOnStartup())
            {
                QaTestClient.StartConnect();
                return;
            }

            if (!QaTestClient.ShouldAutoCreateClient())
            {
                return;
            }

            if (UnityEngine.Object.FindObjectOfType<QaTestClient>() != null)
            {
                return;
            }

            // Temporarily disabled: do not auto-create QaTestClient during startup.
            // QaTestClient.CreateClientObject();
        }
    }
}
