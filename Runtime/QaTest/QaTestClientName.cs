using UnityEngine;

namespace QaTestFramework
{
    public static class QaTestClientName
    {
        public static bool Set(string clientName, bool persist = false, bool resendRegister = true)
        {
            QaTestClient client = ResolveClient();
            if (client == null)
            {
                return false;
            }

            client.SetClientName(clientName, persist, resendRegister);
            return true;
        }

        public static bool Clear(bool persist = false, bool resendRegister = true)
        {
            QaTestClient client = ResolveClient();
            if (client == null)
            {
                return false;
            }

            client.ClearClientName(persist, resendRegister);
            return true;
        }

        public static string GetCustom()
        {
            QaTestClient client = ResolveClient();
            return client != null ? client.CustomClientName : string.Empty;
        }

        public static string GetResolved()
        {
            QaTestClient client = ResolveClient();
            return client != null ? client.ResolvedClientName : string.Empty;
        }

        public static bool RefreshRegistration()
        {
            QaTestClient client = ResolveClient();
            if (client == null)
            {
                return false;
            }

            client.RefreshRegistration();
            return true;
        }

        private static QaTestClient ResolveClient()
        {
            if (QaTestClient.Instance != null)
            {
                return QaTestClient.Instance;
            }

            return Object.FindObjectOfType<QaTestClient>();
        }
    }
}
