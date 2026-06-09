using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace QaTestFramework
{
    public sealed class QaTestClient : MonoBehaviour, IQaTestClientName
    {
        private const string ClientConfigFileName = "qatest.config.txt";
        private const string ClientIdConfigKey = "clientId";
        private const string ClientNameConfigKey = "clientName";
        private const int ClientIdLength = 32;
        public const string ServerIPPlayerPrefsKey = "QaTest.ServerIP";
        public const string ServerPortPlayerPrefsKey = "QaTest.ServerPort";
        public const string ClientIdPlayerPrefsKey = "QaTest.ClientId";
        public const string ClientNamePlayerPrefsKey = "QaTest.ClientName";
        public const string AutoConnectOnStartupPlayerPrefsKey = "QaTest.AutoConnectOnStartup";
        private const string ServerIPKey = ServerIPPlayerPrefsKey;
        private const string ServerPortKey = ServerPortPlayerPrefsKey;
        private const string ClientIdKey = ClientIdPlayerPrefsKey;
        private const string ClientNameKey = ClientNamePlayerPrefsKey;
        private const string AutoConnectOnStartupKey = AutoConnectOnStartupPlayerPrefsKey;
        private const string DefaultServerIP = "localhost";
        private const int DefaultServerPort = 3000;
        private const string ServerScheme = "ws";
        private const string ServerPath = "/ws";
        private const string ServerRole = "unity";
        private const string DuplicateClientNameErrorCode = "duplicate_client_name";
        public const string EnabledPlayerPrefsKey = "QaTest.Enabled";
        public const string EnabledEnvironmentVariable = "QA_TEST_ENABLED";
        private const string EnabledKey = EnabledPlayerPrefsKey;
        private const string EnabledEnvironmentKey = EnabledEnvironmentVariable;
        private static bool hasGlobalRuntimeEnabledOverride;
        private static bool globalRuntimeEnabledOverride;
        private static string globalRuntimeEnabledSource = "Runtime";

        [SerializeField] private bool enableInEditor = true;
        [SerializeField] private bool enableInPlayer = false;
        [SerializeField, InspectorName("ServerIP")] private string serverIP = DefaultServerIP;
        [SerializeField, InspectorName("ServerPort")] private int serverPort = DefaultServerPort;
        [SerializeField] private string clientName = "";
        [SerializeField] private float reconnectDelaySeconds = 2f;
        [SerializeField] private float heartbeatSeconds = 10f;
        [SerializeField] private float connectTimeoutSeconds = 10f;

        private readonly QaTestRegistry registry = new QaTestRegistry();
        private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private readonly object executionStateLock = new object();
        private readonly Dictionary<string, ActiveExecution> activeExecutions = new Dictionary<string, ActiveExecution>();

        private CancellationTokenSource lifetimeCts;
        private ClientWebSocket webSocket;
        private string clientId;
        private float nextHeartbeatAt;
        private string resolvedServerUrl = "";
        private string localIpAddress = "";
        private string[] localIpAddresses = new string[0];
        private string connectionState = "Disabled";
        private string lastError = "";
        private string lastServerMessageType = "";
        private DateTime lastConnectAttemptAtUtc;
        private DateTime lastConnectedAtUtc;
        private DateTime lastDisconnectedAtUtc;
        private DateTime lastRegisteredAtUtc;
        private DateTime lastRegisteredAckAtUtc;
        private DateTime lastHeartbeatSentAtUtc;
        private DateTime lastHeartbeatAckAtUtc;
        private DateTime lastHeartbeatFailedAtUtc;
        private DateTime lastMessageReceivedAtUtc;
        private DateTime lastCommandReceivedAtUtc;
        private DateTime lastResultSentAtUtc;
        private bool qaEnabled;
        private bool fatalConnectionError;
        private bool hasRuntimeEnabledOverride;
        private bool runtimeEnabledOverride;
        private string runtimeEnabledSource = "Runtime";
        private string enabledSource = "Not evaluated";
        private string currentRequestId = "";
        private string currentMethodName = "";
        private int connectAttemptCount;
        private int connectSuccessCount;
        private int reconnectFailureCount;
        private int registerSentCount;
        private int registerFailureCount;
        private int registeredAckCount;
        private int heartbeatSentCount;
        private int heartbeatAckCount;
        private int heartbeatFailureCount;
        private int messagesReceivedCount;
        private int commandsReceivedCount;
        private int resultsSentCount;
        private int resultSendFailureCount;
        private int registeredMethodCount;

        public static QaTestClient Instance { get; private set; }

        public string CustomClientName
        {
            get { return clientName; }
        }

        public string ResolvedClientName
        {
            get { return ResolveClientName(); }
        }

        public string ClientId
        {
            get { return clientId ?? string.Empty; }
        }

        public string ResolvedServerUrl
        {
            get { return string.IsNullOrWhiteSpace(resolvedServerUrl) ? BuildServerUrl() : resolvedServerUrl; }
        }

        public string LocalIpAddress
        {
            get { return localIpAddress; }
        }

        public string ConnectionState
        {
            get { return connectionState; }
        }

        public string SocketState
        {
            get { return webSocket != null ? webSocket.State.ToString() : "None"; }
        }

        public bool IsSocketConnected
        {
            get { return IsConnected; }
        }

        public string LastError
        {
            get { return lastError; }
        }

        public string LastServerMessageType
        {
            get { return lastServerMessageType; }
        }

        public DateTime LastConnectAttemptAtUtc
        {
            get { return lastConnectAttemptAtUtc; }
        }

        public DateTime LastConnectedAtUtc
        {
            get { return lastConnectedAtUtc; }
        }

        public DateTime LastDisconnectedAtUtc
        {
            get { return lastDisconnectedAtUtc; }
        }

        public DateTime LastRegisteredAtUtc
        {
            get { return lastRegisteredAtUtc; }
        }

        public DateTime LastRegisteredAckAtUtc
        {
            get { return lastRegisteredAckAtUtc; }
        }

        public DateTime LastHeartbeatSentAtUtc
        {
            get { return lastHeartbeatSentAtUtc; }
        }

        public DateTime LastHeartbeatAckAtUtc
        {
            get { return lastHeartbeatAckAtUtc; }
        }

        public DateTime LastHeartbeatFailedAtUtc
        {
            get { return lastHeartbeatFailedAtUtc; }
        }

        public DateTime LastMessageReceivedAtUtc
        {
            get { return lastMessageReceivedAtUtc; }
        }

        public DateTime LastCommandReceivedAtUtc
        {
            get { return lastCommandReceivedAtUtc; }
        }

        public DateTime LastResultSentAtUtc
        {
            get { return lastResultSentAtUtc; }
        }

        public int ConnectAttemptCount
        {
            get { return connectAttemptCount; }
        }

        public int ConnectSuccessCount
        {
            get { return connectSuccessCount; }
        }

        public int ReconnectFailureCount
        {
            get { return reconnectFailureCount; }
        }

        public int RegisterSentCount
        {
            get { return registerSentCount; }
        }

        public int RegisterFailureCount
        {
            get { return registerFailureCount; }
        }

        public int RegisteredAckCount
        {
            get { return registeredAckCount; }
        }

        public int HeartbeatSentCount
        {
            get { return heartbeatSentCount; }
        }

        public int HeartbeatAckCount
        {
            get { return heartbeatAckCount; }
        }

        public int HeartbeatFailureCount
        {
            get { return heartbeatFailureCount; }
        }

        public int MessagesReceivedCount
        {
            get { return messagesReceivedCount; }
        }

        public int CommandsReceivedCount
        {
            get { return commandsReceivedCount; }
        }

        public int ResultsSentCount
        {
            get { return resultsSentCount; }
        }

        public int ResultSendFailureCount
        {
            get { return resultSendFailureCount; }
        }

        public int PendingMainThreadActionCount
        {
            get { return mainThreadActions.Count; }
        }

        public bool IsBusy
        {
            get { return GetExecutionState().busy; }
        }

        public string CurrentRequestId
        {
            get { return GetExecutionState().requestId; }
        }

        public string CurrentMethodName
        {
            get { return GetExecutionState().methodName; }
        }

        public int RegisteredMethodCount
        {
            get { return registeredMethodCount; }
        }

        public bool QaEnabled
        {
            get { return qaEnabled; }
        }

        public string EnabledSource
        {
            get { return enabledSource; }
        }

        public float NextHeartbeatInSeconds
        {
            get { return IsConnected ? Mathf.Max(0f, nextHeartbeatAt - Time.unscaledTime) : 0f; }
        }

        private void Reset()
        {
            serverIP = NormalizeServerIP(serverIP);
            serverPort = NormalizeServerPort(serverPort);
            clientName = NormalizeClientName(clientName);
        }

        private void OnValidate()
        {
            serverIP = NormalizeServerIP(serverIP);
            serverPort = NormalizeServerPort(serverPort);
            clientName = NormalizeClientName(clientName);
        }

        private void Awake()
        {
            QaTestClient[] clients = FindObjectsOfType<QaTestClient>();
            if (clients.Length > 1)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            RefreshEnabledState();
            InitializeClientIdentity();
            AssignDefaultClientNameIfEmpty();
            RefreshLocalIpAddresses();

            if (!qaEnabled)
            {
                connectionState = "DisabledByConfig";
                enabled = false;
            }
        }

        private void OnEnable()
        {
            RefreshEnabledState();
            if (!qaEnabled)
            {
                connectionState = "DisabledByConfig";
                enabled = false;
                return;
            }

            connectionState = "Starting";
            lastError = "";
            lifetimeCts = new CancellationTokenSource();
            _ = ConnectionLoopAsync(lifetimeCts.Token);
        }

        private void Update()
        {
            while (mainThreadActions.TryDequeue(out Action action))
            {
                action();
            }

            if (Time.unscaledTime >= nextHeartbeatAt && IsConnected)
            {
                nextHeartbeatAt = Time.unscaledTime + heartbeatSeconds;
                _ = SendHeartbeatAsync();
            }
        }

        private void OnDisable()
        {
            connectionState = qaEnabled ? "Disabled" : "DisabledByConfig";
            lastDisconnectedAtUtc = DateTime.UtcNow;
            lifetimeCts?.Cancel();
            _ = CloseSocketAsync();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            lifetimeCts?.Cancel();
            lifetimeCts?.Dispose();
            sendLock.Dispose();
        }

        private bool IsConnected
        {
            get { return webSocket != null && webSocket.State == WebSocketState.Open; }
        }

        private void InitializeClientIdentity()
        {
            if (Application.isEditor)
            {
                string inspectorClientName = NormalizeClientName(clientName);
                string legacyClientName = NormalizeClientName(PlayerPrefs.GetString(ClientNameKey, inspectorClientName));
                QaTestClientConfig clientConfig = LoadOrCreateClientConfig(inspectorClientName, legacyClientName);
                clientId = clientConfig.ClientId;
                clientName = NormalizeClientName(clientConfig.ClientName);
                AssignDefaultClientNameIfEmpty();
                if (clientConfig.Exists)
                {
                    WriteClientConfig(GetClientConfigPath(), clientId, ResolveClientName());
                }

                return;
            }

            clientId = GetOrCreatePlayerPrefsClientId();
            clientName = NormalizeClientName(PlayerPrefs.GetString(ClientNameKey, string.Empty));
        }

        public void SetClientName(string newClientName, bool persist = false, bool resendRegister = true)
        {
            clientName = NormalizeClientName(newClientName);

            if (persist)
            {
                PersistClientName(clientName);
            }

            if (resendRegister)
            {
                RefreshRegistration();
            }
        }

        public bool ApplyLocalClientNameConfig(bool resendRegister = false)
        {
            if (!Application.isEditor)
            {
                clientId = GetOrCreatePlayerPrefsClientId();
                clientName = NormalizeClientName(PlayerPrefs.GetString(ClientNameKey, string.Empty));
                AssignDefaultClientNameIfEmpty();

                if (resendRegister)
                {
                    RefreshRegistration();
                }

                return PlayerPrefs.HasKey(ClientNameKey);
            }

            QaTestClientConfig config = ReadClientConfig(GetClientConfigPath());
            if (!config.Exists)
            {
                return false;
            }

            if (IsValidClientId(config.ClientId))
            {
                clientId = config.ClientId;
            }
            else if (!IsValidClientId(clientId))
            {
                clientId = GenerateClientId();
            }

            clientName = NormalizeClientName(config.ClientName);
            AssignDefaultClientNameIfEmpty();

            if (resendRegister)
            {
                RefreshRegistration();
            }

            return true;
        }

        [QaTest("设置客户端名称", "设置并保存当前 QA 客户端名称，传空字符串会恢复为默认名称。")]
        public string SetQaClientName([QaParam("新的客户端名称，传空字符串恢复默认名称。")] string newClientName)
        {
            SetClientName(newClientName, true, true);
            return JsonUtility.ToJson(new QaTestClientNameResult
            {
                clientId = clientId,
                clientName = ResolveClientName(),
                configPath = Application.isEditor ? GetClientConfigPath() : string.Empty,
                storage = Application.isEditor ? "qatest.config.txt" : "PlayerPrefs",
            });
        }

        public void ClearClientName(bool persist = false, bool resendRegister = true)
        {
            SetClientName(string.Empty, persist, resendRegister);
        }

        public static void SetIpAndPort(string ip, int port)
        {
            string normalizedIP = NormalizeServerIP(ip);
            int normalizedPort = NormalizeServerPort(port);
            SaveIpAndPortToPlayerPrefs(normalizedIP, normalizedPort);

            QaTestClient client = Instance != null ? Instance : FindObjectOfType<QaTestClient>(true);
            if (client != null)
            {
                client.serverIP = normalizedIP;
                client.serverPort = normalizedPort;
                client.RestartConnectionIfActive();
            }
        }

        public static void GetIpAndPort(out string ip, out int port)
        {
            QaTestClient client = Instance != null ? Instance : FindObjectOfType<QaTestClient>(true);
            string fallbackIP = client != null ? client.serverIP : DefaultServerIP;
            int fallbackPort = client != null ? client.serverPort : DefaultServerPort;

            ip = PlayerPrefs.GetString(ServerIPKey, NormalizeServerIP(fallbackIP));
            port = PlayerPrefs.HasKey(ServerPortKey) ? PlayerPrefs.GetInt(ServerPortKey, NormalizeServerPort(fallbackPort)) : NormalizeServerPort(fallbackPort);
            ip = NormalizeServerIP(ip);
            port = NormalizeServerPort(port);
        }

        public static void SetClientName(string newClientName)
        {
            string normalizedClientName = NormalizeClientName(newClientName);

            QaTestClient client = Instance != null ? Instance : FindObjectOfType<QaTestClient>(true);
            if (client != null)
            {
                client.SetClientName(normalizedClientName, true, true);
                return;
            }

            SaveClientNameToPlayerPrefs(normalizedClientName);
        }

        public static string GetClientName()
        {
            QaTestClient client = Instance != null ? Instance : FindObjectOfType<QaTestClient>(true);
            if (Application.isEditor)
            {
                if (client != null)
                {
                    return client.ResolvedClientName;
                }

                QaTestClientConfig config = ReadClientConfig(GetClientConfigPath());
                string editorClientId = IsValidClientId(config.ClientId) ? config.ClientId : GenerateClientId();
                string editorClientName = NormalizeClientName(config.ClientName);
                return string.IsNullOrWhiteSpace(editorClientName) ? GetDefaultClientName(editorClientId) : editorClientName;
            }

            string persistedClientName = NormalizeClientName(PlayerPrefs.GetString(ClientNameKey, string.Empty));
            if (!string.IsNullOrWhiteSpace(persistedClientName))
            {
                return persistedClientName;
            }

            if (client != null)
            {
                return client.ResolvedClientName;
            }

            string resolvedClientId = GetOrCreatePlayerPrefsClientId();
            return GetDefaultClientName(resolvedClientId);
        }

        public static void SetAutoConnectOnStartup(bool enabled)
        {
            PlayerPrefs.SetInt(AutoConnectOnStartupKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static bool GetAutoConnectOnStartup()
        {
            return PlayerPrefs.HasKey(AutoConnectOnStartupKey) && PlayerPrefs.GetInt(AutoConnectOnStartupKey, 0) != 0;
        }

        public static QaTestClient StartConnect()
        {
            QaTestClient client = Instance != null ? Instance : FindObjectOfType<QaTestClient>(true);
            bool wasEnabled = client != null && client.enabled;
            string previousServerUrl = client != null ? client.GetConfiguredServerUrlSnapshot() : string.Empty;
            string previousClientName = client != null ? client.ResolveClientName() : string.Empty;

            if (client == null)
            {
                client = CreateClientObject();
            }
            else
            {
                client.ApplyPlayerPrefsConfiguration();
            }

            client.ApplyClientEnabled(true, true);
            if (wasEnabled)
            {
                string currentServerUrl = client.GetConfiguredServerUrlSnapshot();
                string currentClientName = client.ResolveClientName();
                if (!previousServerUrl.Equals(currentServerUrl, StringComparison.Ordinal))
                {
                    client.RestartConnectionIfActive();
                }
                else if (!previousClientName.Equals(currentClientName, StringComparison.Ordinal))
                {
                    client.RefreshRegistration();
                }
            }

            return client;
        }

        public static QaTestClient StartConnect(string ip, int port, string newClientName)
        {
            SaveIpAndPortToPlayerPrefs(NormalizeServerIP(ip), NormalizeServerPort(port));
            SaveClientNameToPlayerPrefs(NormalizeClientName(newClientName));
            return StartConnect();
        }

        public void SetClientEnabled(bool isEnabled, bool persist = true)
        {
            ApplyClientEnabled(isEnabled, persist);
        }

        public static bool ShouldAutoCreateClient()
        {
            return ResolveEnabled(true, false).enabled;
        }

        public static void SetGlobalEnabled(bool isEnabled, bool persist = true)
        {
            hasGlobalRuntimeEnabledOverride = true;
            globalRuntimeEnabledOverride = isEnabled;
            globalRuntimeEnabledSource = persist ? "Runtime+PlayerPrefs:" + EnabledKey : "Runtime";

            QaTestClient client = Instance != null ? Instance : FindObjectOfType<QaTestClient>(true);
            if (client == null && isEnabled)
            {
                client = CreateClientObject();
            }

            if (client != null)
            {
                if (isEnabled && !Application.isEditor)
                {
                    client.ApplyPlayerPrefsConfiguration();
                }

                client.ApplyClientEnabled(isEnabled, persist);
            }
            else if (persist)
            {
                PlayerPrefs.SetInt(EnabledKey, isEnabled ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static void ClearGlobalEnabled()
        {
            hasGlobalRuntimeEnabledOverride = false;
            globalRuntimeEnabledOverride = false;
            globalRuntimeEnabledSource = "Runtime";
            PlayerPrefs.DeleteKey(EnabledKey);
            PlayerPrefs.Save();

            QaTestClient client = Instance != null ? Instance : FindObjectOfType<QaTestClient>(true);
            if (client != null)
            {
                client.hasRuntimeEnabledOverride = false;
                client.RefreshEnabledState();
                if (!client.qaEnabled && client.enabled)
                {
                    client.enabled = false;
                }
                else if (client.qaEnabled && !client.enabled)
                {
                    client.enabled = true;
                }
            }
        }

        internal static QaTestClient CreateClientObject()
        {
            QaTestClient existingClient = Instance != null ? Instance : FindObjectOfType<QaTestClient>(true);
            if (existingClient != null)
            {
                return existingClient;
            }

            GameObject clientObject = new GameObject("[QaTestClient]");
            return clientObject.AddComponent<QaTestClient>();
        }

        public void RefreshRegistration()
        {
            if (!IsConnected)
            {
                return;
            }

            registry.Refresh();
            registeredMethodCount = registry.Methods.Count;
            CancellationToken token = lifetimeCts != null ? lifetimeCts.Token : CancellationToken.None;
            _ = SendRegisterSafeAsync(token);
        }

        private void ApplyPlayerPrefsConfiguration()
        {
            GetIpAndPort(out string resolvedIP, out int resolvedPort);
            serverIP = resolvedIP;
            serverPort = resolvedPort;

            if (!Application.isEditor)
            {
                clientId = GetOrCreatePlayerPrefsClientId();
                clientName = NormalizeClientName(PlayerPrefs.GetString(ClientNameKey, string.Empty));
                AssignDefaultClientNameIfEmpty();
                return;
            }

            AssignDefaultClientNameIfEmpty();
        }

        private string GetConfiguredServerUrlSnapshot()
        {
            string commandLineServerUrl = GetCommandLineServerUrl();
            if (!string.IsNullOrWhiteSpace(commandLineServerUrl))
            {
                return EnsureUnityRole(commandLineServerUrl);
            }

            return BuildConfiguredServerUrl(serverIP, serverPort);
        }

        private void RestartConnectionIfActive()
        {
            resolvedServerUrl = string.Empty;
            if (!qaEnabled || !enabled)
            {
                return;
            }

            lifetimeCts?.Cancel();
            _ = CloseSocketAsync();
            enabled = false;
            enabled = true;
        }

        private async Task ConnectionLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await ConnectAsync(token);
                    await ReceiveLoopAsync(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException exception)
                {
                    reconnectFailureCount++;
                    connectionState = "Cancelled";
                    lastError = exception.Message;
                    Debug.LogWarning("[QaTest] WebSocket connection cancelled: " + exception.Message);
                }
                catch (Exception exception)
                {
                    reconnectFailureCount++;
                    connectionState = "Failed";
                    lastError = exception.GetType().Name + ": " + exception.Message;
                    Debug.LogWarning("[QaTest] WebSocket connection failed: " + exception.Message);
                }

                if (fatalConnectionError)
                {
                    break;
                }

                if (!token.IsCancellationRequested)
                {
                    connectionState = "Reconnecting";
                    lastDisconnectedAtUtc = DateTime.UtcNow;
                }

                await CloseSocketAsync();

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Mathf.Max(0.5f, reconnectDelaySeconds)), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task ConnectAsync(CancellationToken token)
        {
            await CloseSocketAsync();

            fatalConnectionError = false;
            webSocket = new ClientWebSocket();
            resolvedServerUrl = BuildServerUrl();
            Uri uri = new Uri(resolvedServerUrl);
            connectAttemptCount++;
            lastConnectAttemptAtUtc = DateTime.UtcNow;
            connectionState = "Connecting";
            lastError = "";
            Debug.Log("[QaTest] Connecting to " + uri);

            float timeoutSeconds = Mathf.Max(0f, connectTimeoutSeconds);
            using (CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                if (timeoutSeconds > 0f)
                {
                    connectCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                }

                try
                {
                    await webSocket.ConnectAsync(uri, connectCts.Token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested && timeoutSeconds > 0f)
                {
                    throw new TimeoutException("WebSocket connect timed out after " + timeoutSeconds.ToString("0.###") + " seconds.");
                }
            }

            connectSuccessCount++;
            lastConnectedAtUtc = DateTime.UtcNow;
            connectionState = "Connected";
            Debug.Log("[QaTest] Connected.");

            registry.Refresh();
            registeredMethodCount = registry.Methods.Count;
            try
            {
                await SendRegisterAsync(token);
            }
            catch (Exception exception)
            {
                registerFailureCount++;
                connectionState = "RegisterFailed";
                lastError = exception.GetType().Name + ": " + exception.Message;
                throw;
            }

            connectionState = "Registered";
            nextHeartbeatAt = Time.unscaledTime + heartbeatSeconds;
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            byte[] buffer = new byte[8192];

            while (!token.IsCancellationRequested && IsConnected)
            {
                using (MemoryStream messageStream = new MemoryStream())
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            connectionState = "ClosedByServer";
                            lastDisconnectedAtUtc = DateTime.UtcNow;
                            return;
                        }

                        messageStream.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    string messageJson = Encoding.UTF8.GetString(messageStream.ToArray());
                    messagesReceivedCount++;
                    lastMessageReceivedAtUtc = DateTime.UtcNow;
                    HandleServerMessage(messageJson);
                }
            }
        }

        private void HandleServerMessage(string messageJson)
        {
            QaTestServerCommand command = JsonUtility.FromJson<QaTestServerCommand>(messageJson);
            lastServerMessageType = command != null && !string.IsNullOrWhiteSpace(command.type) ? command.type : "unknown";
            if (lastServerMessageType == "registered")
            {
                registeredAckCount++;
                lastRegisteredAckAtUtc = DateTime.UtcNow;
            }
            else if (lastServerMessageType == "heartbeat_ack")
            {
                heartbeatAckCount++;
                lastHeartbeatAckAtUtc = DateTime.UtcNow;
            }
            else if (lastServerMessageType == "error")
            {
                HandleServerError(command);
            }

            if (command != null && command.type == "refresh_methods")
            {
                mainThreadActions.Enqueue(RefreshRegistration);
                return;
            }

            if (command == null || command.type != "execute")
            {
                return;
            }

            commandsReceivedCount++;
            lastCommandReceivedAtUtc = DateTime.UtcNow;
            mainThreadActions.Enqueue(() => { _ = TryExecuteAndReportAsync(command); });
        }

        private void HandleServerError(QaTestServerCommand command)
        {
            string errorMessage = command != null && !string.IsNullOrWhiteSpace(command.error)
                ? command.error
                : "Server returned an error.";
            string errorCode = command != null ? command.code ?? string.Empty : string.Empty;
            bool isDuplicateClientNameError = errorCode.Equals(DuplicateClientNameErrorCode, StringComparison.OrdinalIgnoreCase);
            if (isDuplicateClientNameError)
            {
                errorMessage = FormatDuplicateClientNameError(command, errorMessage);
            }

            lastError = isDuplicateClientNameError || string.IsNullOrWhiteSpace(errorCode) ? errorMessage : errorCode + ": " + errorMessage;
            Debug.LogError("[QaTest] " + lastError);

            if (command == null || !command.fatal)
            {
                return;
            }

            fatalConnectionError = true;
            connectionState = "FatalError";
            lifetimeCts?.Cancel();
            _ = CloseSocketAsync();

            if (isDuplicateClientNameError)
            {
                Debug.LogError("[QaTest] 客户端名称重复，QA 客户端将停止连接并不再重连。");
            }

            mainThreadActions.Enqueue(StopUnityStartup);
        }

        private string FormatDuplicateClientNameError(QaTestServerCommand command, string fallbackMessage)
        {
            string duplicateName = command != null ? NormalizeClientName(command.clientName) : string.Empty;
            if (string.IsNullOrWhiteSpace(duplicateName))
            {
                duplicateName = ResolveClientName();
            }

            if (!string.IsNullOrWhiteSpace(duplicateName))
            {
                return "QaTest 客户端名称“" + duplicateName + "”已存在，当前连接已被拒绝。请修改 QaTestClient 的 clientName 后重试。";
            }

            return !string.IsNullOrWhiteSpace(fallbackMessage)
                ? fallbackMessage
                : "QaTest 客户端名称已存在，当前连接已被拒绝。请修改 QaTestClient 的 clientName 后重试。";
        }

        private void StopUnityStartup()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorApplication.isPlaying)
            {
                UnityEditor.EditorApplication.isPlaying = false;
            }
#else
            Application.Quit(1);
#endif
        }

        private async Task TryExecuteAndReportAsync(QaTestServerCommand command)
        {
            bool allowParallelExecution = command != null && command.allowParallelExecution;
            QaTestMethodEntry resolvedMethod = null;
            Exception resolveException = null;

            if (allowParallelExecution || !IsBusy)
            {
                try
                {
                    resolvedMethod = ResolveMethod(command);
                    allowParallelExecution = allowParallelExecution || resolvedMethod.AllowParallelExecution;
                }
                catch (Exception exception)
                {
                    resolveException = exception;
                }
            }

            if (!TryBeginExecution(command, allowParallelExecution))
            {
                await SendBusyResultAsync(command);
                return;
            }

            if (IsConnected)
            {
                _ = SendHeartbeatAsync();
            }

            try
            {
                await ExecuteAndReportAsync(command, resolvedMethod, resolveException);
            }
            finally
            {
                EndExecution(command);
                if (IsConnected)
                {
                    _ = SendHeartbeatAsync();
                }
            }
        }

        private bool TryBeginExecution(QaTestServerCommand command, bool allowParallelExecution)
        {
            lock (executionStateLock)
            {
                string requestId = GetRequestStateId(command);
                if (activeExecutions.ContainsKey(requestId))
                {
                    return false;
                }

                if (!allowParallelExecution && activeExecutions.Count > 0)
                {
                    return false;
                }

                activeExecutions[requestId] = new ActiveExecution
                {
                    requestId = requestId,
                    methodName = GetCommandMethodName(command),
                };
                RefreshCurrentExecutionStateLocked();
                return true;
            }
        }

        private void EndExecution(QaTestServerCommand command)
        {
            EndExecution(GetRequestStateId(command));
        }

        private void EndExecution(string requestId)
        {
            lock (executionStateLock)
            {
                if (!string.IsNullOrEmpty(requestId))
                {
                    activeExecutions.Remove(requestId);
                }

                RefreshCurrentExecutionStateLocked();
            }
        }

        private async Task SendBusyResultAsync(QaTestServerCommand command)
        {
            ExecutionState executionState = GetExecutionState();
            QaTestResultMessage resultMessage = new QaTestResultMessage
            {
                requestId = command.requestId,
                clientId = clientId,
                methodId = command.methodId,
                methodName = command.methodName,
                success = false,
                result = string.Empty,
                error = "QaTestClient is busy running request " + executionState.requestId + ".",
                durationMs = 0,
            };
            ApplyExecutionState(resultMessage);

            try
            {
                CancellationToken token = lifetimeCts != null ? lifetimeCts.Token : CancellationToken.None;
                await SendMessageAsync(resultMessage, token);
                resultsSentCount++;
                lastResultSentAtUtc = DateTime.UtcNow;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                resultSendFailureCount++;
                lastError = exception.GetType().Name + ": " + exception.Message;
                Debug.LogWarning("[QaTest] Failed to send busy result: " + exception.Message);
            }
        }

        private async Task ExecuteAndReportAsync(QaTestServerCommand command, QaTestMethodEntry resolvedMethod, Exception resolveException)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            QaTestResultMessage resultMessage = new QaTestResultMessage
            {
                requestId = command.requestId,
                clientId = clientId,
                methodId = command.methodId,
                methodName = command.methodName,
            };

            try
            {
                if (resolveException != null)
                {
                    throw resolveException;
                }

                QaTestMethodEntry method = resolvedMethod ?? ResolveMethod(command);
                resultMessage.methodId = method.Id;
                resultMessage.methodName = method.DisplayName;
                object invocationResult = method.Invoke(command.arguments);
                resultMessage.result = await ResolveInvocationResultAsync(invocationResult);
                resultMessage.success = true;
                ApplyResultSemantics(resultMessage);
            }
            catch (TargetInvocationException exception)
            {
                Exception inner = exception.InnerException ?? exception;
                resultMessage.success = false;
                resultMessage.error = inner.GetType().Name + ": " + inner.Message;
            }
            catch (Exception exception)
            {
                resultMessage.success = false;
                resultMessage.error = exception.GetType().Name + ": " + exception.Message;
            }
            finally
            {
                stopwatch.Stop();
                resultMessage.durationMs = (int)stopwatch.ElapsedMilliseconds;
                EndExecution(command);
                ApplyExecutionState(resultMessage);
                try
                {
                    CancellationToken token = lifetimeCts != null ? lifetimeCts.Token : CancellationToken.None;
                    await SendMessageAsync(resultMessage, token);
                    resultsSentCount++;
                    lastResultSentAtUtc = DateTime.UtcNow;
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    resultSendFailureCount++;
                    lastError = exception.GetType().Name + ": " + exception.Message;
                    Debug.LogWarning("[QaTest] Failed to send result: " + exception.Message);
                }
            }
        }

        private QaTestMethodEntry ResolveMethod(QaTestServerCommand command)
        {
            string lookupKey = string.IsNullOrWhiteSpace(command.methodId) ? command.methodName : command.methodId;
            bool hadStaleTarget = false;

            if (registry.TryGet(lookupKey, out QaTestMethodEntry method))
            {
                if (method.IsTargetAvailable)
                {
                    return method;
                }

                hadStaleTarget = true;
            }

            registry.Refresh();
            registeredMethodCount = registry.Methods.Count;
            if (registry.TryGet(lookupKey, out method) && method.IsTargetAvailable)
            {
                return method;
            }

            string reason = hadStaleTarget
                ? "QaTest method target is no longer available"
                : "QaTest method not found";
            throw new InvalidOperationException(reason + ": " + lookupKey);
        }

        private async Task<string> ResolveInvocationResultAsync(object invocationResult)
        {
            if (invocationResult == null)
            {
                return string.Empty;
            }

            Task task = invocationResult as Task;
            if (task != null)
            {
                await task;
                Type taskType = invocationResult.GetType();
                if (taskType.IsGenericType)
                {
                    PropertyInfo resultProperty = taskType.GetProperty("Result");
                    object result = resultProperty != null ? resultProperty.GetValue(invocationResult) : null;
                    return ConvertResultToString(result);
                }

                return "Task completed";
            }

            QaTestCoroutineResult coroutineResult = invocationResult as QaTestCoroutineResult;
            if (coroutineResult != null)
            {
                object yieldedResult = await RunRoutineAsync(coroutineResult.Routine);
                object finalResult = coroutineResult.HasResultFactory ? coroutineResult.GetResult() : yieldedResult;
                return finalResult != null ? ConvertResultToString(finalResult) : "Coroutine completed";
            }

            IEnumerator routine = invocationResult as IEnumerator;
            if (routine != null)
            {
                object result = await RunRoutineAsync(routine);
                return result != null ? ConvertResultToString(result) : "Coroutine completed";
            }

            return ConvertResultToString(invocationResult);
        }

        private static void ApplyResultSemantics(QaTestResultMessage resultMessage)
        {
            if (!resultMessage.success)
            {
                return;
            }

            string failureReason;
            if (!TryGetBusinessFailure(resultMessage.result, out failureReason))
            {
                return;
            }

            resultMessage.success = false;
            if (string.IsNullOrWhiteSpace(resultMessage.error))
            {
                resultMessage.error = failureReason;
            }
        }

        private static bool TryGetBusinessFailure(string result, out string failureReason)
        {
            failureReason = string.Empty;
            string text = result != null ? result.Trim() : string.Empty;
            if (text.Length == 0)
            {
                return false;
            }

            if (text.StartsWith("failed:", StringComparison.OrdinalIgnoreCase))
            {
                failureReason = text.Substring("failed:".Length).Trim();
                if (string.IsNullOrEmpty(failureReason))
                {
                    failureReason = "QaTest method returned failed.";
                }

                return true;
            }

            if (text.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                failureReason = "QaTest method returned False.";
                return true;
            }

            QaTestStructuredResult structuredResult;
            if (TryParseStructuredResult(text, out structuredResult))
            {
                if (IsStructuredFailure(text, structuredResult, out failureReason))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseStructuredResult(string text, out QaTestStructuredResult structuredResult)
        {
            structuredResult = null;
            if (string.IsNullOrWhiteSpace(text) || text[0] != '{')
            {
                return false;
            }

            if (!ContainsJsonField(text, "ok")
                && !ContainsJsonField(text, "status")
                && !ContainsJsonField(text, "code")
                && !ContainsJsonField(text, "message")
                && !ContainsJsonField(text, "error"))
            {
                return false;
            }

            try
            {
                structuredResult = JsonUtility.FromJson<QaTestStructuredResult>(text);
                return structuredResult != null;
            }
            catch
            {
                structuredResult = null;
                return false;
            }
        }

        private static bool IsStructuredFailure(
            string rawJson,
            QaTestStructuredResult structuredResult,
            out string failureReason)
        {
            failureReason = string.Empty;
            bool hasOk = ContainsJsonField(rawJson, "ok");
            string status = structuredResult.status ?? string.Empty;
            bool failedStatus = status.Equals("failed", StringComparison.OrdinalIgnoreCase)
                || status.Equals("failure", StringComparison.OrdinalIgnoreCase)
                || status.Equals("error", StringComparison.OrdinalIgnoreCase)
                || status.Equals("unsupported", StringComparison.OrdinalIgnoreCase)
                || status.Equals("cancelled", StringComparison.OrdinalIgnoreCase)
                || status.Equals("canceled", StringComparison.OrdinalIgnoreCase);

            if (!failedStatus && (!hasOk || structuredResult.ok))
            {
                return false;
            }

            failureReason = FirstNonEmpty(
                structuredResult.message,
                structuredResult.error,
                structuredResult.code,
                status,
                "QaTest structured result failed.");
            return true;
        }

        private static bool ContainsJsonField(string json, string fieldName)
        {
            return json.IndexOf("\"" + fieldName + "\"", StringComparison.Ordinal) >= 0;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return values[i].Trim();
                }
            }

            return string.Empty;
        }

        private Task<object> RunRoutineAsync(IEnumerator routine)
        {
            TaskCompletionSource<object> completion = new TaskCompletionSource<object>();
            StartCoroutine(RunRoutine(routine, completion));
            return completion.Task;
        }

        private IEnumerator RunRoutine(IEnumerator routine, TaskCompletionSource<object> completion)
        {
            object routineResult = null;
            while (true)
            {
                object current;
                try
                {
                    if (!routine.MoveNext())
                    {
                        break;
                    }

                    current = routine.Current;
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                    yield break;
                }

                QaTestCoroutineReturn returnedValue = current as QaTestCoroutineReturn;
                if (returnedValue != null)
                {
                    routineResult = returnedValue.Value;
                    continue;
                }

                yield return current;
            }

            completion.TrySetResult(routineResult);
        }

        private async Task SendRegisterAsync(CancellationToken token)
        {
            RefreshLocalIpAddresses();
            QaTestRegisterMessage registerMessage = new QaTestRegisterMessage
            {
                clientId = clientId,
                name = ResolveClientName(),
                ipAddress = localIpAddress,
                ipAddresses = localIpAddresses,
                platform = Application.platform.ToString(),
                unityVersion = Application.unityVersion,
                deviceName = GetMachineName(),
                operatingSystem = GetOperatingSystemName(),
                methods = registry.ToDtos(),
            };
            ApplyExecutionState(registerMessage);

            await SendMessageAsync(registerMessage, token);
            registerSentCount++;
            registeredMethodCount = registerMessage.methods != null ? registerMessage.methods.Length : 0;
            lastRegisteredAtUtc = DateTime.UtcNow;
        }

        private async Task SendHeartbeatAsync()
        {
            try
            {
                QaTestHeartbeatMessage heartbeatMessage = new QaTestHeartbeatMessage
                {
                    clientId = clientId,
                };
                ApplyExecutionState(heartbeatMessage);

                CancellationToken token = lifetimeCts != null ? lifetimeCts.Token : CancellationToken.None;
                await SendMessageAsync(heartbeatMessage, token);
                heartbeatSentCount++;
                lastHeartbeatSentAtUtc = DateTime.UtcNow;
                connectionState = "Registered";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                heartbeatFailureCount++;
                lastHeartbeatFailedAtUtc = DateTime.UtcNow;
                connectionState = "HeartbeatFailed";
                lastError = exception.GetType().Name + ": " + exception.Message;
                Debug.LogWarning("[QaTest] Failed to send heartbeat: " + exception.Message);
            }
        }

        private async Task SendRegisterSafeAsync(CancellationToken token)
        {
            try
            {
                await SendRegisterAsync(token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                registerFailureCount++;
                lastError = exception.GetType().Name + ": " + exception.Message;
                Debug.LogWarning("[QaTest] Failed to refresh registration: " + exception.Message);
            }
        }

        private async Task SendMessageAsync<T>(T message, CancellationToken token)
        {
            if (!IsConnected)
            {
                return;
            }

            string json = JsonUtility.ToJson(message);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            await sendLock.WaitAsync(token);
            try
            {
                if (IsConnected)
                {
                    await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
                }
            }
            finally
            {
                sendLock.Release();
            }
        }

        private async Task CloseSocketAsync()
        {
            ClientWebSocket socket = webSocket;
            webSocket = null;

            if (socket == null)
            {
                return;
            }

            try
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "QaTest client closing", CancellationToken.None);
                }
            }
            catch
            {
                // Socket teardown is best-effort during reconnects and play-mode shutdown.
            }
            finally
            {
                socket.Dispose();
            }
        }

        private string BuildServerUrl()
        {
            string resolvedUrl = GetCommandLineServerUrl();
            if (!string.IsNullOrWhiteSpace(resolvedUrl))
            {
                return EnsureUnityRole(resolvedUrl);
            }

            string resolvedIP = PlayerPrefs.GetString(ServerIPKey, serverIP);
            int resolvedPort = PlayerPrefs.HasKey(ServerPortKey) ? PlayerPrefs.GetInt(ServerPortKey, serverPort) : serverPort;
            return BuildConfiguredServerUrl(resolvedIP, resolvedPort);
        }

        private static string BuildConfiguredServerUrl(string ip, int port)
        {
            string normalizedIP = NormalizeServerIP(ip);
            int normalizedPort = NormalizeServerPort(port);
            return ServerScheme + "://" + FormatHostForUrl(normalizedIP) + ":" + normalizedPort + ServerPath + "?role=" + ServerRole;
        }

        private static string NormalizeServerIP(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? DefaultServerIP : value.Trim();
        }

        private static int NormalizeServerPort(int value)
        {
            return Mathf.Clamp(value, 1, 65535);
        }

        private static string FormatHostForUrl(string host)
        {
            if (host.IndexOf(':') >= 0 && !host.StartsWith("[", StringComparison.Ordinal) && !host.EndsWith("]", StringComparison.Ordinal))
            {
                return "[" + host + "]";
            }

            return host;
        }

        private static string EnsureUnityRole(string url)
        {
            string normalizedUrl = url.Trim();
            int fragmentIndex = normalizedUrl.IndexOf('#');
            string fragment = fragmentIndex >= 0 ? normalizedUrl.Substring(fragmentIndex) : string.Empty;
            string urlWithoutFragment = fragmentIndex >= 0 ? normalizedUrl.Substring(0, fragmentIndex) : normalizedUrl;
            int queryIndex = urlWithoutFragment.IndexOf('?');
            if (queryIndex < 0)
            {
                return urlWithoutFragment + "?role=" + ServerRole + fragment;
            }

            string baseUrl = urlWithoutFragment.Substring(0, queryIndex);
            string query = urlWithoutFragment.Substring(queryIndex + 1);
            string[] parameters = query.Split('&');
            List<string> normalizedParameters = new List<string>();
            foreach (string parameter in parameters)
            {
                if (string.IsNullOrEmpty(parameter))
                {
                    continue;
                }

                string key = parameter.Split(new[] { '=' }, 2)[0];
                if (key.Equals("role", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                normalizedParameters.Add(parameter);
            }

            normalizedParameters.Add("role=" + ServerRole);
            return baseUrl + "?" + string.Join("&", normalizedParameters.ToArray()) + fragment;
        }

        private void ApplyClientEnabled(bool isEnabled, bool persist)
        {
            if (persist)
            {
                PlayerPrefs.SetInt(EnabledKey, isEnabled ? 1 : 0);
                PlayerPrefs.Save();
            }

            hasRuntimeEnabledOverride = true;
            runtimeEnabledOverride = isEnabled;
            runtimeEnabledSource = persist ? "Runtime+PlayerPrefs:" + EnabledKey : "Runtime";

            qaEnabled = isEnabled;
            enabledSource = runtimeEnabledSource;

            if (isEnabled)
            {
                if (!enabled)
                {
                    enabled = true;
                }
            }
            else
            {
                connectionState = "DisabledByConfig";
                lifetimeCts?.Cancel();
                _ = CloseSocketAsync();
                if (enabled)
                {
                    enabled = false;
                }
            }
        }

        private void RefreshEnabledState()
        {
            if (hasRuntimeEnabledOverride)
            {
                qaEnabled = runtimeEnabledOverride;
                enabledSource = runtimeEnabledSource;
                return;
            }

            EnabledResolution resolution = ResolveEnabled(enableInEditor, enableInPlayer);
            qaEnabled = resolution.enabled;
            enabledSource = resolution.source;
        }

        private string ResolveClientName()
        {
            if (!string.IsNullOrWhiteSpace(clientName))
            {
                return clientName;
            }

            return GetDefaultClientName();
        }

        private void PersistClientName(string normalizedClientName)
        {
            if (string.IsNullOrWhiteSpace(normalizedClientName))
            {
                PlayerPrefs.DeleteKey(ClientNameKey);
            }
            else
            {
                PlayerPrefs.SetString(ClientNameKey, normalizedClientName);
            }

            PlayerPrefs.Save();

            if (!Application.isEditor)
            {
                if (!IsValidClientId(clientId))
                {
                    clientId = GetOrCreatePlayerPrefsClientId();
                }

                return;
            }

            string configPath = GetClientConfigPath();
            string persistedClientId = IsValidClientId(clientId)
                ? clientId
                : ReadClientConfig(configPath).ClientId;
            if (!IsValidClientId(persistedClientId))
            {
                persistedClientId = GenerateClientId();
            }

            if (!IsValidClientId(clientId))
            {
                clientId = persistedClientId;
            }

            string persistedClientName = string.IsNullOrWhiteSpace(normalizedClientName)
                ? GetDefaultClientName(persistedClientId)
                : normalizedClientName;
            WriteClientConfig(configPath, persistedClientId, persistedClientName);
        }

        private static void SaveIpAndPortToPlayerPrefs(string ip, int port)
        {
            PlayerPrefs.SetString(ServerIPKey, NormalizeServerIP(ip));
            PlayerPrefs.SetInt(ServerPortKey, NormalizeServerPort(port));
            PlayerPrefs.Save();
        }

        private static void SaveClientNameToPlayerPrefs(string normalizedClientName)
        {
            if (string.IsNullOrWhiteSpace(normalizedClientName))
            {
                PlayerPrefs.DeleteKey(ClientNameKey);
            }
            else
            {
                PlayerPrefs.SetString(ClientNameKey, normalizedClientName);
            }

            PlayerPrefs.Save();
        }

        private static string GetOrCreatePlayerPrefsClientId()
        {
            string persistedClientId = PlayerPrefs.GetString(ClientIdKey, string.Empty);
            string normalizedClientId = NormalizeClientId(persistedClientId);
            if (!IsValidClientId(normalizedClientId))
            {
                normalizedClientId = GenerateClientId();
            }

            if (!persistedClientId.Equals(normalizedClientId, StringComparison.Ordinal))
            {
                PlayerPrefs.SetString(ClientIdKey, normalizedClientId);
                PlayerPrefs.Save();
            }

            return normalizedClientId;
        }

        private void AssignDefaultClientNameIfEmpty()
        {
            clientName = NormalizeClientName(clientName);
            if (string.IsNullOrWhiteSpace(clientName) || IsLegacyDefaultClientName(clientName))
            {
                clientName = GetDefaultClientName();
            }
        }

        private string GetDefaultClientName()
        {
            return GetDefaultClientName(clientId);
        }

        private static string GetDefaultClientName(string sourceClientId)
        {
            string normalizedClientId = NormalizeClientName(sourceClientId);
            if (normalizedClientId.Length >= 8)
            {
                return normalizedClientId.Substring(0, 8);
            }

            if (!string.IsNullOrWhiteSpace(normalizedClientId))
            {
                return normalizedClientId;
            }

            return "QaTestClient";
        }

        private static string GetUnityProjectName()
        {
            string productName = NormalizeClientName(Application.productName);
            if (!string.IsNullOrWhiteSpace(productName))
            {
                return productName;
            }

            try
            {
                string dataPath = Application.dataPath;
                if (!string.IsNullOrWhiteSpace(dataPath))
                {
                    DirectoryInfo projectDirectory = Directory.GetParent(dataPath);
                    if (projectDirectory != null)
                    {
                        return NormalizeClientName(projectDirectory.Name);
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static bool IsLegacyDefaultClientName(string value)
        {
            string normalized = NormalizeClientName(value);
            string projectName = GetUnityProjectName();

            return !string.IsNullOrWhiteSpace(normalized)
                && (normalized.Equals("QaTestClient", StringComparison.Ordinal)
                    || normalized.Equals(NormalizeClientName(SystemInfo.deviceName), StringComparison.Ordinal)
                    || normalized.Equals(NormalizeClientName(Environment.MachineName), StringComparison.Ordinal)
                    || (!string.IsNullOrWhiteSpace(projectName)
                        && normalized.StartsWith(projectName + " / ", StringComparison.Ordinal)));
        }

        private static string GetMachineName()
        {
            return FirstNonEmpty(
                GetAndroidDeviceName(),
                NormalizeClientName(SystemInfo.deviceName),
                NormalizeClientName(Environment.MachineName));
        }

        private static string GetOperatingSystemName()
        {
            return NormalizeClientName(SystemInfo.operatingSystem);
        }

        private static string GetAndroidDeviceName()
        {
            if (Application.platform != RuntimePlatform.Android)
            {
                return string.Empty;
            }

            string deviceName = ReadAndroidSettingsValue("android.provider.Settings$Global", "device_name");
            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                return deviceName;
            }

            string bluetoothName = ReadAndroidSettingsValue("android.provider.Settings$Secure", "bluetooth_name");
            if (!string.IsNullOrWhiteSpace(bluetoothName))
            {
                return bluetoothName;
            }

            return NormalizeClientName(SystemInfo.deviceModel);
        }

        private static string ReadAndroidSettingsValue(string settingsClassName, string key)
        {
            try
            {
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject contentResolver = currentActivity.Call<AndroidJavaObject>("getContentResolver"))
                using (AndroidJavaClass settingsClass = new AndroidJavaClass(settingsClassName))
                {
                    return NormalizeClientName(settingsClass.CallStatic<string>("getString", contentResolver, key));
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private void RefreshLocalIpAddresses()
        {
            localIpAddresses = GetLocalIpAddresses();
            localIpAddress = localIpAddresses.Length > 0 ? localIpAddresses[0] : string.Empty;
        }

        private static string[] GetLocalIpAddresses()
        {
            List<string> addresses = new List<string>();

            try
            {
                IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (IPAddress address in host.AddressList)
                {
                    if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
                    {
                        continue;
                    }

                    string value = address.ToString();
                    if (!addresses.Contains(value))
                    {
                        addresses.Add(value);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[QaTest] Failed to resolve local IP address: " + exception.Message);
            }

            return addresses.ToArray();
        }

        private static string GetCommandLineServerUrl()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.StartsWith("--qa-server-url=", StringComparison.OrdinalIgnoreCase))
                {
                    return arg.Substring("--qa-server-url=".Length);
                }

                if (arg.Equals("--qa-server-url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    return args[i + 1];
                }
            }

            return string.Empty;
        }

        private static EnabledResolution ResolveEnabled(bool editorDefault, bool playerDefault)
        {
            if (hasGlobalRuntimeEnabledOverride)
            {
                return new EnabledResolution { enabled = globalRuntimeEnabledOverride, source = globalRuntimeEnabledSource };
            }

            bool parsedValue;
            string source;
            if (TryGetCommandLineEnabled(out parsedValue, out source))
            {
                return new EnabledResolution { enabled = parsedValue, source = source };
            }

            string environmentValue = Environment.GetEnvironmentVariable(EnabledEnvironmentKey);
            if (TryParseBoolean(environmentValue, out parsedValue))
            {
                return new EnabledResolution { enabled = parsedValue, source = "Environment:" + EnabledEnvironmentKey };
            }

            if (PlayerPrefs.HasKey(EnabledKey))
            {
                return new EnabledResolution { enabled = PlayerPrefs.GetInt(EnabledKey, 0) != 0, source = "PlayerPrefs:" + EnabledKey };
            }

            bool defaultValue = Application.isEditor ? editorDefault : playerDefault;
            return new EnabledResolution { enabled = defaultValue, source = Application.isEditor ? "Inspector:enableInEditor" : "Inspector:enableInPlayer" };
        }

        private static bool TryGetCommandLineEnabled(out bool value, out string source)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.Equals("--qa-enable", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--qa-enabled", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--qa-test-enable", StringComparison.OrdinalIgnoreCase))
                {
                    value = true;
                    source = "CommandLine:" + arg;
                    return true;
                }

                if (arg.Equals("--qa-disable", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--qa-disabled", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--qa-test-disable", StringComparison.OrdinalIgnoreCase))
                {
                    value = false;
                    source = "CommandLine:" + arg;
                    return true;
                }

                string inlinePrefix = "--qa-enabled=";
                if (arg.StartsWith(inlinePrefix, StringComparison.OrdinalIgnoreCase) &&
                    TryParseBoolean(arg.Substring(inlinePrefix.Length), out value))
                {
                    source = "CommandLine:" + inlinePrefix;
                    return true;
                }

                inlinePrefix = "--qa-test-enabled=";
                if (arg.StartsWith(inlinePrefix, StringComparison.OrdinalIgnoreCase) &&
                    TryParseBoolean(arg.Substring(inlinePrefix.Length), out value))
                {
                    source = "CommandLine:" + inlinePrefix;
                    return true;
                }

                if ((arg.Equals("--qa-enabled", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("--qa-test-enabled", StringComparison.OrdinalIgnoreCase)) &&
                    i + 1 < args.Length &&
                    TryParseBoolean(args[i + 1], out value))
                {
                    source = "CommandLine:" + arg;
                    return true;
                }
            }

            value = false;
            source = string.Empty;
            return false;
        }

        private static bool TryParseBoolean(string rawValue, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return false;
            }

            string normalized = rawValue.Trim();
            if (normalized.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("enabled", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }

            if (normalized.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("disabled", StringComparison.OrdinalIgnoreCase))
            {
                value = false;
                return true;
            }

            return false;
        }

        private static QaTestClientConfig LoadOrCreateClientConfig(string inspectorClientName, string fallbackClientName)
        {
            string configPath = GetClientConfigPath();
            QaTestClientConfig config = ReadClientConfig(configPath);
            if (!IsValidClientId(config.ClientId))
            {
                config.ClientId = GenerateClientId();
            }

            if (Application.isEditor)
            {
                return config;
            }

            string normalizedInspectorClientName = NormalizeClientName(inspectorClientName);
            if (!string.IsNullOrWhiteSpace(normalizedInspectorClientName))
            {
                config.ClientName = normalizedInspectorClientName;
            }
            else if (string.IsNullOrWhiteSpace(config.ClientName))
            {
                config.ClientName = NormalizeClientName(fallbackClientName);
            }

            return config;
        }

        private static QaTestClientConfig ReadClientConfig(string configPath)
        {
            QaTestClientConfig config = new QaTestClientConfig();
            try
            {
                if (!File.Exists(configPath))
                {
                    return config;
                }

                config.Exists = true;
                string[] lines = File.ReadAllLines(configPath, Encoding.UTF8);
                foreach (string line in lines)
                {
                    string trimmed = line != null ? line.Trim() : string.Empty;
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int separatorIndex = trimmed.IndexOf('=');
                    string value = separatorIndex >= 0 ? trimmed.Substring(separatorIndex + 1).Trim() : trimmed;
                    string key = separatorIndex >= 0 ? trimmed.Substring(0, separatorIndex).Trim() : ClientIdConfigKey;
                    if (key.Equals(ClientIdConfigKey, StringComparison.OrdinalIgnoreCase))
                    {
                        config.ClientId = NormalizeClientId(value);
                        continue;
                    }

                    if (key.Equals(ClientNameConfigKey, StringComparison.OrdinalIgnoreCase))
                    {
                        config.ClientName = NormalizeClientName(value);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[QaTest] Failed to read client config: " + exception.Message);
            }

            return config;
        }

        private static void WriteClientConfig(string configPath, string configClientId, string configClientName)
        {
            try
            {
                string directory = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(
                    configPath,
                    ClientIdConfigKey + "=" + NormalizeClientId(configClientId) + Environment.NewLine +
                    ClientNameConfigKey + "=" + NormalizeClientName(configClientName) + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[QaTest] Failed to write client config: " + exception.Message);
            }
        }

        private static string GetClientConfigPath()
        {
            return Path.Combine(GetClientConfigDirectory(), ClientConfigFileName);
        }

        private static string GetClientConfigDirectory()
        {
            try
            {
                if (Application.isEditor)
                {
                    string dataPath = Application.dataPath;
                    if (!string.IsNullOrWhiteSpace(dataPath))
                    {
                        DirectoryInfo projectDirectory = Directory.GetParent(dataPath);
                        if (projectDirectory != null && !string.IsNullOrWhiteSpace(projectDirectory.FullName))
                        {
                            return projectDirectory.FullName;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(Application.persistentDataPath))
                {
                    return Application.persistentDataPath;
                }

                if (!string.IsNullOrWhiteSpace(Application.dataPath))
                {
                    DirectoryInfo dataParent = Directory.GetParent(Application.dataPath);
                    return dataParent != null ? dataParent.FullName : Application.dataPath;
                }
            }
            catch
            {
            }

            return Environment.CurrentDirectory;
        }

        private static string GenerateClientId()
        {
            string source = BuildClientIdSeed(Guid.NewGuid().ToString("N"));
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(source));
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }

                return NormalizeClientId(builder.ToString());
            }
        }

        private static string BuildClientIdSeed(string randomValue)
        {
            if (Application.isEditor)
            {
                return "editor:" + NormalizeConfigPath(GetClientConfigDirectory()) + ":" + randomValue;
            }

            return "player:" +
                NormalizeClientName(Application.identifier) + ":" +
                Application.platform + ":" +
                randomValue;
        }

        private static string NormalizeConfigPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
        }

        private static bool IsValidClientId(string value)
        {
            string normalized = NormalizeClientId(value);
            if (normalized.Length != ClientIdLength)
            {
                return false;
            }

            for (int i = 0; i < normalized.Length; i++)
            {
                char character = normalized[i];
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class QaTestClientConfig
        {
            public bool Exists;
            public string ClientId = string.Empty;
            public string ClientName = string.Empty;
        }

        private static string NormalizeClientName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string NormalizeClientId(string value)
        {
            string normalized = NormalizeClientName(value).Replace("-", string.Empty).ToLowerInvariant();
            return normalized.Length > ClientIdLength
                ? normalized.Substring(0, ClientIdLength)
                : normalized;
        }

        private static string GetRequestStateId(QaTestServerCommand command)
        {
            return command != null && !string.IsNullOrWhiteSpace(command.requestId)
                ? command.requestId
                : "(missing requestId)";
        }

        private static string GetCommandMethodName(QaTestServerCommand command)
        {
            if (command == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(command.methodName)
                ? command.methodId ?? string.Empty
                : command.methodName;
        }

        private void RefreshCurrentExecutionStateLocked()
        {
            if (activeExecutions.Count == 0)
            {
                currentRequestId = string.Empty;
                currentMethodName = string.Empty;
                return;
            }

            if (activeExecutions.Count == 1)
            {
                foreach (ActiveExecution execution in activeExecutions.Values)
                {
                    currentRequestId = execution.requestId;
                    currentMethodName = execution.methodName;
                    return;
                }
            }

            currentRequestId = string.Join(",", activeExecutions.Keys);
            currentMethodName = activeExecutions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " active requests";
        }

        private ExecutionState GetExecutionState()
        {
            lock (executionStateLock)
            {
                return new ExecutionState
                {
                    busy = !string.IsNullOrEmpty(currentRequestId),
                    requestId = currentRequestId,
                    methodName = currentMethodName,
                };
            }
        }

        private void ApplyExecutionState(QaTestRegisterMessage message)
        {
            ExecutionState executionState = GetExecutionState();
            message.busy = executionState.busy;
            message.currentRequestId = executionState.requestId;
            message.currentMethodName = executionState.methodName;
        }

        private void ApplyExecutionState(QaTestHeartbeatMessage message)
        {
            ExecutionState executionState = GetExecutionState();
            message.busy = executionState.busy;
            message.currentRequestId = executionState.requestId;
            message.currentMethodName = executionState.methodName;
        }

        private void ApplyExecutionState(QaTestResultMessage message)
        {
            ExecutionState executionState = GetExecutionState();
            message.busy = executionState.busy;
            message.currentRequestId = executionState.requestId;
            message.currentMethodName = executionState.methodName;
        }

        private static string ConvertResultToString(object result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            string stringResult = result as string;
            if (stringResult != null)
            {
                return stringResult;
            }

            if (result is UnityEngine.Object unityObject)
            {
                return unityObject.name;
            }

            return Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture);
        }

        private struct ExecutionState
        {
            public bool busy;
            public string requestId;
            public string methodName;
        }

        private struct ActiveExecution
        {
            public string requestId;
            public string methodName;
        }

        private struct EnabledResolution
        {
            public bool enabled;
            public string source;
        }
    }
}
