using System;
using QaTestFramework;
using UnityEditor;
using UnityEngine;

namespace QaTestFramework.Editor
{
    [CustomEditor(typeof(QaTestClient))]
    public sealed class QaTestClientEditor : UnityEditor.Editor
    {
        private const string RuntimeDiagnosticsFoldoutKey = "QaTestFramework.QaTestClientEditor.RuntimeDiagnosticsExpanded";
        private bool runtimeDiagnosticsExpanded;

        private void OnEnable()
        {
            runtimeDiagnosticsExpanded = SessionState.GetBool(RuntimeDiagnosticsFoldoutKey, true);
            QaTestClient client = (QaTestClient)target;
            if (!Application.isPlaying && client.ApplyLocalClientNameConfig(false))
            {
                EditorUtility.SetDirty(client);
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty clientNameProperty = serializedObject.FindProperty("clientName");
            string previousClientName = clientNameProperty != null ? clientNameProperty.stringValue : string.Empty;

            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            QaTestClient client = (QaTestClient)target;
            string currentClientName = clientNameProperty != null ? clientNameProperty.stringValue : string.Empty;
            if (!previousClientName.Equals(currentClientName, StringComparison.Ordinal))
            {
                client.SetClientName(currentClientName, true, Application.isPlaying);
            }

            EditorGUILayout.Space(8f);
            DrawRuntimeStatus(client, ref runtimeDiagnosticsExpanded);
            SessionState.SetBool(RuntimeDiagnosticsFoldoutKey, runtimeDiagnosticsExpanded);
            DrawActions(client);

            if (Application.isPlaying)
            {
                Repaint();
            }
        }

        private static void DrawRuntimeStatus(QaTestClient client, ref bool diagnosticsExpanded)
        {
            diagnosticsExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(diagnosticsExpanded, "Runtime Diagnostics");
            if (!diagnosticsExpanded)
            {
                EditorGUILayout.EndFoldoutHeaderGroup();
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Connection State", client.ConnectionState);
                EditorGUILayout.TextField("Socket State", client.SocketState);
                EditorGUILayout.Toggle("QA Enabled", client.QaEnabled);
                EditorGUILayout.TextField("Enabled Source", client.EnabledSource);
                EditorGUILayout.Toggle("Socket Connected", client.IsSocketConnected);
                EditorGUILayout.TextField("Client Id", client.ClientId);
                EditorGUILayout.TextField("Resolved Name", client.ResolvedClientName);
                EditorGUILayout.TextField("IP Address", client.LocalIpAddress);
                EditorGUILayout.TextField("Resolved Server Url", client.ResolvedServerUrl);
                EditorGUILayout.IntField("Registered Methods", client.RegisteredMethodCount);
                EditorGUILayout.IntField("Pending Main Thread Actions", client.PendingMainThreadActionCount);
                EditorGUILayout.Toggle("Busy", client.IsBusy);
                EditorGUILayout.TextField("Current Request Id", client.CurrentRequestId);
                EditorGUILayout.TextField("Current Method", client.CurrentMethodName);
                EditorGUILayout.FloatField("Next Heartbeat In", client.NextHeartbeatInSeconds);
                EditorGUILayout.TextField("Last Server Message", client.LastServerMessageType);
                EditorGUILayout.TextField("Last Error", client.LastError);
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Timestamps", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Last Connect Attempt", FormatTime(client.LastConnectAttemptAtUtc));
                EditorGUILayout.TextField("Last Connected", FormatTime(client.LastConnectedAtUtc));
                EditorGUILayout.TextField("Last Disconnected", FormatTime(client.LastDisconnectedAtUtc));
                EditorGUILayout.TextField("Last Register Sent", FormatTime(client.LastRegisteredAtUtc));
                EditorGUILayout.TextField("Last Register Ack", FormatTime(client.LastRegisteredAckAtUtc));
                EditorGUILayout.TextField("Last Heartbeat Sent", FormatTime(client.LastHeartbeatSentAtUtc));
                EditorGUILayout.TextField("Last Heartbeat Ack", FormatTime(client.LastHeartbeatAckAtUtc));
                EditorGUILayout.TextField("Last Heartbeat Failed", FormatTime(client.LastHeartbeatFailedAtUtc));
                EditorGUILayout.TextField("Last Message Received", FormatTime(client.LastMessageReceivedAtUtc));
                EditorGUILayout.TextField("Last Command Received", FormatTime(client.LastCommandReceivedAtUtc));
                EditorGUILayout.TextField("Last Result Sent", FormatTime(client.LastResultSentAtUtc));
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Counters", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Connect Attempts", client.ConnectAttemptCount);
                EditorGUILayout.IntField("Connect Successes", client.ConnectSuccessCount);
                EditorGUILayout.IntField("Reconnect Failures", client.ReconnectFailureCount);
                EditorGUILayout.IntField("Register Sent", client.RegisterSentCount);
                EditorGUILayout.IntField("Register Failures", client.RegisterFailureCount);
                EditorGUILayout.IntField("Register Ack", client.RegisteredAckCount);
                EditorGUILayout.IntField("Heartbeat Sent", client.HeartbeatSentCount);
                EditorGUILayout.IntField("Heartbeat Ack", client.HeartbeatAckCount);
                EditorGUILayout.IntField("Heartbeat Failures", client.HeartbeatFailureCount);
                EditorGUILayout.IntField("Messages Received", client.MessagesReceivedCount);
                EditorGUILayout.IntField("Commands Received", client.CommandsReceivedCount);
                EditorGUILayout.IntField("Results Sent", client.ResultsSentCount);
                EditorGUILayout.IntField("Result Send Failures", client.ResultSendFailureCount);
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private static void DrawActions(QaTestClient client)
        {
            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    if (GUILayout.Button("Refresh Registration"))
                    {
                        client.RefreshRegistration();
                    }
                }

                if (GUILayout.Button("Copy Client Id"))
                {
                    EditorGUIUtility.systemCopyBuffer = client.ClientId;
                }
            }

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Enable QA"))
                {
                    client.SetClientEnabled(true);
                }

                if (GUILayout.Button("Disable QA"))
                {
                    client.SetClientEnabled(false);
                }
            }
        }

        private static string FormatTime(DateTime utc)
        {
            if (utc == default(DateTime))
            {
                return "-";
            }

            return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
}
