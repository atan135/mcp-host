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

        private readonly QaTestRegistry registry = new QaTestRegistry();
        private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource lifetimeCts;
        private ClientWebSocket webSocket;
        private string clientId;
        private float nextHeartbeatAt;

        public static QaTestClient Instance { get; private set; }

        public string CustomClientName
        {
            get { return clientName; }
        }

        public string ResolvedClientName
        {
            get { return ResolveClientName(); }
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
        }

        private void OnEnable()
        {
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
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[QaTest] WebSocket connection failed: " + exception.Message);
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
            Uri uri = new Uri(BuildServerUrl());
            Debug.Log("[QaTest] Connecting to " + uri);
            await webSocket.ConnectAsync(uri, token);
            Debug.Log("[QaTest] Connected.");

            registry.Refresh();
            await SendRegisterAsync(token);
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
                            return;
                        }

                        messageStream.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    string messageJson = Encoding.UTF8.GetString(messageStream.ToArray());
                    HandleServerMessage(messageJson);
                }
            }
        }

        private void HandleServerMessage(string messageJson)
        {
            QaTestServerCommand command = JsonUtility.FromJson<QaTestServerCommand>(messageJson);
            if (command == null || command.type != "execute")
            {
                return;
            }

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
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
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
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
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
