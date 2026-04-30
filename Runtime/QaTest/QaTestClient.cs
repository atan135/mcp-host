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

        [SerializeField] private string serverUrl = "ws://localhost:3000/ws?role=unity";
        [SerializeField] private string clientName = "";
        [SerializeField] private float reconnectDelaySeconds = 2f;
        [SerializeField] private float heartbeatSeconds = 10f;
        [SerializeField] private float connectTimeoutSeconds = 10f;

        private readonly QaTestRegistry registry = new QaTestRegistry();
        private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);

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

        public int RegisteredMethodCount
        {
            get { return registeredMethodCount; }
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
            clientId = LoadOrCreateClientId();
            clientName = PlayerPrefs.GetString(ClientNameKey, clientName);
            registry.Refresh();
            registeredMethodCount = registry.Methods.Count;
        }

        private void OnEnable()
        {
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
            connectionState = "Disabled";
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
            mainThreadActions.Enqueue(() => { _ = ExecuteAndReportAsync(command); });
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
    }
}
