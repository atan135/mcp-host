using System;

namespace QaTestFramework
{
    [Serializable]
    internal sealed class QaTestRegisterMessage
    {
        public string type = "register";
        public string clientId;
        public string name;
        public string platform;
        public string unityVersion;
        public QaTestMethodDto[] methods;
    }

    [Serializable]
    internal sealed class QaTestMethodDto
    {
        public string id;
        public string name;
        public string declaringType;
        public string description;
        public string returnType;
        public bool isStatic;
        public QaTestParameterDto[] parameters;
    }

    [Serializable]
    internal sealed class QaTestParameterDto
    {
        public string name;
        public string type;
        public bool isOptional;
        public string defaultValue;
    }

    [Serializable]
    internal sealed class QaTestServerCommand
    {
        public string type;
        public string requestId;
        public string methodId;
        public string methodName;
        public string[] arguments;
    }

    [Serializable]
    internal sealed class QaTestHeartbeatMessage
    {
        public string type = "heartbeat";
        public string clientId;
    }

    [Serializable]
    internal sealed class QaTestResultMessage
    {
        public string type = "qa_result";
        public string requestId;
        public string clientId;
        public string methodId;
        public string methodName;
        public bool success;
        public string result;
        public string error;
        public int durationMs;
    }
}
