using System;

namespace QaTestFramework
{
    [Serializable]
    internal sealed class QaTestRegisterMessage
    {
        public string type = "register";
        public string clientId;
        public string name;
        public string ipAddress;
        public string[] ipAddresses;
        public string platform;
        public string unityVersion;
        public string deviceName;
        public string operatingSystem;
        public bool busy;
        public string currentRequestId;
        public string currentMethodName;
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
        public bool allowParallelExecution;
        public QaTestParameterDto[] parameters;
    }

    [Serializable]
    internal sealed class QaTestParameterDto
    {
        public string name;
        public string type;
        public string description;
        public bool isOptional;
        public bool isRequired;
        public string defaultValue;
    }

    [Serializable]
    internal sealed class QaTestServerCommand
    {
        public string type;
        public bool fatal;
        public string code;
        public string error;
        public string clientId;
        public string clientName;
        public string existingClientId;
        public string requestId;
        public string methodId;
        public string methodName;
        public bool allowParallelExecution;
        public string[] arguments;
    }

    [Serializable]
    internal sealed class QaTestHeartbeatMessage
    {
        public string type = "heartbeat";
        public string clientId;
        public bool busy;
        public string currentRequestId;
        public string currentMethodName;
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
        public bool busy;
        public string currentRequestId;
        public string currentMethodName;
    }

    [Serializable]
    internal sealed class QaTestStructuredResult
    {
        public bool ok;
        public string status;
        public string code;
        public string message;
        public string error;
    }

    [Serializable]
    internal sealed class QaTestClientNameResult
    {
        public string clientId;
        public string clientName;
        public string configPath;
        public string storage;
    }
}
