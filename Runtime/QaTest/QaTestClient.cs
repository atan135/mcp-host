using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace QaTestFramework
{
    public sealed class QaTestClient : MonoBehaviour, IQaTestClientName
    {
        private const string ClientIdKey = "QaTest.ClientId";
        private const string ServerUrlKey = "QaTest.ServerUrl";
        private const string ClientNameKey = "QaTest.ClientName";
        public const string EnabledPlayerPrefsKey = "QaTest.Enabled";
        public const string EnabledEnvironmentVariable = "QA_TEST_ENABLED";
        private const string EnabledKey = EnabledPlayerPrefsKey;
        private const string EnabledEnvironmentKey = EnabledEnvironmentVariable;
        private static bool hasGlobalRuntimeEnabledOverride;
        private static bool globalRuntimeEnabledOverride;
        private static string globalRuntimeEnabledSource = "Runtime";

        [SerializeField] private bool enableInEditor = true;
        [SerializeField] private bool enableInPlayer = false;
        [SerializeField] private string serverUrl = "ws://localhost:3000/ws?role=unity";
        [SerializeField] private string clientName = "";
        [SerializeField] private float reconnectDelaySeconds = 2f;
        [SerializeField] private float heartbeatSeconds = 10f;
        [SerializeField] private float connectTimeoutSeconds = 10f;

        private readonly QaTestRegistry registry = new QaTestRegistry();
        private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private readonly object executionStateLock = new object();

        private CancellationTokenSource lifetimeCts;
        private ClientWebSocket webSocket;
        private string clientId;
        private float nextHeartbeatAt;
        private string resolvedServerUrl = "";
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
            clientId = LoadOrCreateClientId();
            clientName = PlayerPrefs.GetString(ClientNameKey, clientName);
            registry.Refresh();
            registeredMethodCount = registry.Methods.Count;

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

        public void SetClientName(string newClientName, bool persist = false, bool resendRegister = true)
        {
            clientName = NormalizeClientName(newClientName);

            if (persist)
            {
                if (string.IsNullOrWhiteSpace(clientName))
                {
                    PlayerPrefs.DeleteKey(ClientNameKey);
                }
                else
                {
                    PlayerPrefs.SetString(ClientNameKey, clientName);
                }

                PlayerPrefs.Save();
            }

            if (resendRegister)
            {
                RefreshRegistration();
            }
        }

        public void ClearClientName(bool persist = false, bool resendRegister = true)
        {
            SetClientName(string.Empty, persist, resendRegister);
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

            CancellationToken token = lifetimeCts != null ? lifetimeCts.Token : CancellationToken.None;
            _ = SendRegisterSafeAsync(token);
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

            if (command == null || command.type != "execute")
            {
                return;
            }

            commandsReceivedCount++;
            lastCommandReceivedAtUtc = DateTime.UtcNow;
            mainThreadActions.Enqueue(() => { _ = TryExecuteAndReportAsync(command); });
        }

        private async Task TryExecuteAndReportAsync(QaTestServerCommand command)
        {
            if (!TryBeginExecution(command))
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
                await ExecuteAndReportAsync(command);
            }
            finally
            {
                EndExecution();
                if (IsConnected)
                {
                    _ = SendHeartbeatAsync();
                }
            }
        }

        private bool TryBeginExecution(QaTestServerCommand command)
        {
            lock (executionStateLock)
            {
                if (!string.IsNullOrEmpty(currentRequestId))
                {
                    return false;
                }

                currentRequestId = string.IsNullOrWhiteSpace(command.requestId) ? "(missing requestId)" : command.requestId;
                currentMethodName = string.IsNullOrWhiteSpace(command.methodName) ? command.methodId ?? string.Empty : command.methodName;
                return true;
            }
        }

        private void EndExecution()
        {
            lock (executionStateLock)
            {
                currentRequestId = string.Empty;
                currentMethodName = string.Empty;
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

        private async Task ExecuteAndReportAsync(QaTestServerCommand command)
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
                registry.Refresh();
                string lookupKey = string.IsNullOrWhiteSpace(command.methodId) ? command.methodName : command.methodId;
                if (!registry.TryGet(lookupKey, out QaTestMethodEntry method))
                {
                    throw new InvalidOperationException("QaTest method not found: " + lookupKey);
                }

                resultMessage.methodId = method.Id;
                resultMessage.methodName = method.DisplayName;
                object invocationResult = method.Invoke(command.arguments);
                resultMessage.result = await ResolveInvocationResultAsync(invocationResult);
                resultMessage.success = true;
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
            QaTestRegisterMessage registerMessage = new QaTestRegisterMessage
            {
                clientId = clientId,
                name = ResolveClientName(),
                platform = Application.platform.ToString(),
                unityVersion = Application.unityVersion,
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
            if (string.IsNullOrWhiteSpace(resolvedUrl))
            {
                resolvedUrl = PlayerPrefs.GetString(ServerUrlKey, serverUrl);
            }

            if (resolvedUrl.IndexOf("role=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return resolvedUrl;
            }

            return resolvedUrl.Contains("?") ? resolvedUrl + "&role=unity" : resolvedUrl + "?role=unity";
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

            return Application.productName + "@" + SystemInfo.deviceName;
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

        private static string LoadOrCreateClientId()
        {
            string savedClientId = PlayerPrefs.GetString(ClientIdKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(savedClientId))
            {
                return savedClientId;
            }

            savedClientId = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(ClientIdKey, savedClientId);
            PlayerPrefs.Save();
            return savedClientId;
        }

        private static string NormalizeClientName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
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

        private struct EnabledResolution
        {
            public bool enabled;
            public string source;
        }
    }
}
