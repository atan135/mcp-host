using UnityEngine;

namespace QaTestFramework
{
    public static class QaTestBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateClient()
        {
            if (UnityEngine.Object.FindObjectOfType<QaTestClient>() != null)
            {
                return;
            }

            GameObject clientObject = new GameObject("[QaTestClient]");
            clientObject.AddComponent<QaTestClient>();
            UnityEngine.Object.DontDestroyOnLoad(clientObject);
        }
    }
}
