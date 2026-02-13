using UnityEditor;
using UnityEngine;

namespace Fantasy
{
    [CustomEditor(typeof(FantasyRuntime))]
    public class FantasyRuntimeEditor : Editor
    {
        private SerializedProperty runtimeNameProperty;
        private SerializedProperty remoteIPProperty;
        private SerializedProperty remotePortProperty;
        private SerializedProperty protocolProperty;
        private SerializedProperty enableHttpsProperty;
        private SerializedProperty connectTimeoutProperty;
        private SerializedProperty enableHeartbeatProperty;
        private SerializedProperty heartbeatIntervalProperty;
        private SerializedProperty heartbeatTimeOutProperty;
        private SerializedProperty heartbeatTimeOutIntervalProperty;
        private SerializedProperty maxPingSamplesProperty;
        private SerializedProperty isRuntimeInstanceProperty;
        private SerializedProperty onConnectCompleteProperty;
        private SerializedProperty onConnectFailProperty;
        private SerializedProperty onConnectDisconnectProperty;

        private GUIStyle headerStyle;
        private GUIStyle boxStyle;
        private GUIStyle helpBoxStyle;

        private void OnEnable()
        {
            runtimeNameProperty = serializedObject.FindProperty("runtimeName");
            remoteIPProperty = serializedObject.FindProperty("remoteIP");
            remotePortProperty = serializedObject.FindProperty("remotePort");
            protocolProperty = serializedObject.FindProperty("protocol");
            enableHttpsProperty = serializedObject.FindProperty("enableHttps");
            connectTimeoutProperty = serializedObject.FindProperty("connectTimeout");
            enableHeartbeatProperty = serializedObject.FindProperty("enableHeartbeat");
            heartbeatIntervalProperty = serializedObject.FindProperty("heartbeatInterval");
            heartbeatTimeOutProperty = serializedObject.FindProperty("heartbeatTimeOut");
            heartbeatTimeOutIntervalProperty = serializedObject.FindProperty("heartbeatTimeOutInterval");
            maxPingSamplesProperty = serializedObject.FindProperty("maxPingSamples");
            isRuntimeInstanceProperty = serializedObject.FindProperty("isRuntimeInstance");
            onConnectCompleteProperty = serializedObject.FindProperty("onConnectComplete");
            onConnectFailProperty = serializedObject.FindProperty("onConnectFail");
            onConnectDisconnectProperty = serializedObject.FindProperty("onConnectDisconnect");
        }

        private void InitializeStyles()
        {
            headerStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.8f, 0.9f, 1f) }
            };

            boxStyle ??= new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(10, 10, 10, 10),
                margin = new RectOffset(0, 0, 5, 5)
            };

            helpBoxStyle ??= new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 11,
                padding = new RectOffset(10, 10, 8, 8)
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            InitializeStyles();

            // Header with Fantasy logo text
            DrawFantasyHeader();
            EditorGUILayout.Space(5);

            // Instance Name Box
            DrawInstanceNameBox();
            EditorGUILayout.Space(5);

            // Main Settings Box
            DrawMainSettingsBox();
            EditorGUILayout.Space(5);

            // Connection Settings Box
            DrawConnectionSettingsBox();
            EditorGUILayout.Space(5);

            // Heartbeat Settings Box
            DrawHeartbeatSettingsBox();
            EditorGUILayout.Space(5);

            // Ping Information Box (只在运行时且心跳启用时显示)
            if (Application.isPlaying && enableHeartbeatProperty.boolValue)
            {
                DrawPingInformationBox();
                EditorGUILayout.Space(5);
            }

            // Runtime Instance Settings Box
            DrawRuntimeInstanceSettingsBox();
            EditorGUILayout.Space(5);

            // Event Callbacks Box
            DrawEventCallbacksBox();
            EditorGUILayout.Space(5);

            // Validation Warnings
            DrawValidationWarnings();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawFantasyHeader()
        {
            EditorGUILayout.BeginVertical(boxStyle);

            // Title with icon
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.3f, 0.7f, 1f) }
            };

            EditorGUILayout.LabelField("⚡ Fantasy Runtime", titleStyle);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // Subtitle
            GUIStyle subtitleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.gray }
            };
            EditorGUILayout.LabelField("High-Performance C# Network Framework", subtitleStyle);

            EditorGUILayout.EndVertical();
        }

        private void DrawInstanceNameBox()
        {
            Color originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.9f, 0.7f, 0.3f, 0.3f);

            EditorGUILayout.BeginVertical(boxStyle);
            GUI.backgroundColor = originalColor;

            // Section Header
            DrawSectionHeader("🏷️ Instance Identification");
            EditorGUILayout.Space(8);

            // Runtime Name
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("📛 Runtime Name", "Instance name for identification in logs and when using multiple instances"),
                GUILayout.Width(EditorGUIUtility.labelWidth - 20));

            // Highlight the name field
            Color prevBgColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 1f, 0.8f);
            runtimeNameProperty.stringValue = EditorGUILayout.TextField(runtimeNameProperty.stringValue);
            GUI.backgroundColor = prevBgColor;

            EditorGUILayout.EndHorizontal();

            // Validation
            if (string.IsNullOrWhiteSpace(runtimeNameProperty.stringValue))
            {
                EditorGUILayout.Space(3);
                GUIStyle warningStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.9f, 0.7f, 0.3f) },
                    wordWrap = true
                };
                EditorGUILayout.LabelField("⚠ Name is empty - logs will show blank identifier", warningStyle);
            }
            else
            {
                EditorGUILayout.Space(3);
                GUIStyle infoStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.7f, 0.7f, 0.7f) },
                    wordWrap = true,
                    fontStyle = FontStyle.Italic
                };
                EditorGUILayout.LabelField($"Logs will show: [{runtimeNameProperty.stringValue}] Connection established...", infoStyle);
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.EndVertical();
        }

        private void DrawMainSettingsBox()
        {
            EditorGUILayout.BeginVertical(boxStyle);

            // Section Header
            DrawSectionHeader("🌐 Network Configuration");
            EditorGUILayout.Space(8);

            // Remote IP
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("🖥️ Remote IP", "Server IP address to connect"),
                GUILayout.Width(EditorGUIUtility.labelWidth - 20));
            remoteIPProperty.stringValue = EditorGUILayout.TextField(remoteIPProperty.stringValue);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(3);

            // Remote Port
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("🔌 Remote Port", "Server port number"),
                GUILayout.Width(EditorGUIUtility.labelWidth - 20));
            remotePortProperty.intValue = EditorGUILayout.IntField(remotePortProperty.intValue);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(8);

            // Separator
            DrawSeparator();
            EditorGUILayout.Space(8);

            // Protocol Selection
            DrawSectionHeader("📡 Protocol Settings");
            EditorGUILayout.Space(8);

            FantasyRuntime.NetworkProtocolType currentProtocol = (FantasyRuntime.NetworkProtocolType)protocolProperty.enumValueIndex;

            // Protocol Dropdown with icon
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent(GetProtocolIcon(currentProtocol) + " Protocol Type",
                "Select network protocol"), GUILayout.Width(EditorGUIUtility.labelWidth - 20));

            EditorGUI.BeginChangeCheck();
            FantasyRuntime.NetworkProtocolType newProtocol = (FantasyRuntime.NetworkProtocolType)EditorGUILayout.EnumPopup(currentProtocol);
            if (EditorGUI.EndChangeCheck())
            {
                protocolProperty.enumValueIndex = (int)newProtocol;

                // Auto-disable HTTPS when switching away from WebSocket
                if (newProtocol != FantasyRuntime.NetworkProtocolType.WebSocket && enableHttpsProperty.boolValue)
                {
                    enableHttpsProperty.boolValue = false;
                }
            }
            EditorGUILayout.EndHorizontal();

            // Update current protocol after potential change
            currentProtocol = (FantasyRuntime.NetworkProtocolType)protocolProperty.enumValueIndex;

            // WebSocket HTTPS option
            if (currentProtocol == FantasyRuntime.NetworkProtocolType.WebSocket)
            {
                EditorGUILayout.Space(5);
                DrawWebSocketOptions();
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.EndVertical();
        }

        private void DrawConnectionSettingsBox()
        {
            EditorGUILayout.BeginVertical(boxStyle);

            DrawSectionHeader("⏱️ Connection Settings");
            EditorGUILayout.Space(8);

            // Connection Timeout
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("⏰ Connect Timeout", "Connection timeout in milliseconds"),
                GUILayout.Width(EditorGUIUtility.labelWidth - 20));
            connectTimeoutProperty.intValue = EditorGUILayout.IntField(connectTimeoutProperty.intValue);
            EditorGUILayout.LabelField("ms", GUILayout.Width(25));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(3);
            GUIStyle hintStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f) },
                wordWrap = true
            };
            EditorGUILayout.LabelField("Default: 5000ms (5 seconds)", hintStyle);

            EditorGUILayout.Space(5);
            EditorGUILayout.EndVertical();
        }

        private void DrawHeartbeatSettingsBox()
        {
            Color originalColor = GUI.backgroundColor;
            bool isEnabled = enableHeartbeatProperty.boolValue;

            // Change background color based on enabled state
            GUI.backgroundColor = isEnabled ? new Color(0.3f, 0.6f, 0.4f, 0.3f) : new Color(0.4f, 0.4f, 0.4f, 0.3f);

            EditorGUILayout.BeginVertical(boxStyle);
            GUI.backgroundColor = originalColor;

            // Header with enable toggle
            EditorGUILayout.BeginHorizontal();
            DrawSectionHeader("💓 Heartbeat Settings");
            GUILayout.FlexibleSpace();

            GUIStyle toggleStyle = new GUIStyle(EditorStyles.toggle)
            {
                fontStyle = FontStyle.Bold
            };

            EditorGUILayout.LabelField(new GUIContent("Enable", "Enable heartbeat to keep connection alive"),
                GUILayout.Width(50));
            enableHeartbeatProperty.boolValue = EditorGUILayout.Toggle(enableHeartbeatProperty.boolValue, toggleStyle, GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            // Show heartbeat parameters only when enabled
            if (isEnabled)
            {
                // Heartbeat Interval
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(new GUIContent("📤 Interval", "Heartbeat request send interval in milliseconds"),
                    GUILayout.Width(EditorGUIUtility.labelWidth - 20));
                heartbeatIntervalProperty.intValue = EditorGUILayout.IntField(heartbeatIntervalProperty.intValue);
                EditorGUILayout.LabelField("ms", GUILayout.Width(25));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(3);

                // Heartbeat TimeOut
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(new GUIContent("⏳ TimeOut", "Communication timeout. Session disconnects if exceeded"),
                    GUILayout.Width(EditorGUIUtility.labelWidth - 20));
                heartbeatTimeOutProperty.intValue = EditorGUILayout.IntField(heartbeatTimeOutProperty.intValue);
                EditorGUILayout.LabelField("ms", GUILayout.Width(25));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(3);

                // TimeOut Check Interval
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(new GUIContent("🔍 Check Interval", "Frequency to check connection timeout"),
                    GUILayout.Width(EditorGUIUtility.labelWidth - 20));
                heartbeatTimeOutIntervalProperty.intValue = EditorGUILayout.IntField(heartbeatTimeOutIntervalProperty.intValue);
                EditorGUILayout.LabelField("ms", GUILayout.Width(25));
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(3);

                // Max Ping Samples
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(new GUIContent("📊 Ping Samples", "Number of ping samples for latency calculation"),
                    GUILayout.Width(EditorGUIUtility.labelWidth - 20));
                maxPingSamplesProperty.intValue = EditorGUILayout.IntField(maxPingSamplesProperty.intValue);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(5);
            }
            else
            {
                GUIStyle disabledStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.6f, 0.6f, 0.6f) },
                    fontStyle = FontStyle.Italic,
                    alignment = TextAnchor.MiddleCenter
                };
                EditorGUILayout.LabelField("Heartbeat disabled - Connection may timeout without activity", disabledStyle);
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.EndVertical();
        }

        private void DrawPingInformationBox()
        {
            FantasyRuntime fantasyRuntime = (FantasyRuntime)target;

            Color originalColor = GUI.backgroundColor;

            // 检查连接状态
            bool isDisconnected = fantasyRuntime.Session == null || fantasyRuntime.Session.IsDisposed;

            // 根据连接状态改变背景色
            GUI.backgroundColor = isDisconnected
                ? new Color(0.8f, 0.3f, 0.3f, 0.3f)  // 断开连接 - 红色背景
                : new Color(0.3f, 0.8f, 0.9f, 0.3f); // 已连接 - 蓝色背景

            EditorGUILayout.BeginVertical(boxStyle);
            GUI.backgroundColor = originalColor;

            // Section Header
            DrawSectionHeader("📊 Network Latency (Real-time)");
            EditorGUILayout.Space(8);

            // 如果连接已断开，显示断开状态
            if (isDisconnected)
            {
                GUIStyle disconnectedStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.9f, 0.3f, 0.3f) }
                };
                EditorGUILayout.LabelField("🔴 Connection Disconnected", disconnectedStyle);
                EditorGUILayout.Space(5);

                GUIStyle hintStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.7f, 0.5f, 0.5f) },
                    wordWrap = true,
                    fontStyle = FontStyle.Italic
                };
                EditorGUILayout.LabelField("Session is disposed or not established", hintStyle);

                EditorGUILayout.Space(5);
                EditorGUILayout.EndVertical();

                // 实时刷新以便检测重新连接
                Repaint();
                return;
            }

            try
            {
                // 获取 Ping 值
                float pingSeconds = fantasyRuntime.PingSeconds;
                int pingMilliseconds = fantasyRuntime.PingMilliseconds;

                // 根据延迟显示不同的颜色和状态
                Color statusColor;
                string statusText;
                string statusIcon;

                if (pingMilliseconds < 50)
                {
                    statusColor = new Color(0.3f, 0.9f, 0.3f); // 绿色 - 优秀
                    statusText = "Excellent";
                    statusIcon = "🟢";
                }
                else if (pingMilliseconds < 100)
                {
                    statusColor = new Color(0.5f, 0.9f, 0.3f); // 黄绿色 - 良好
                    statusText = "Good";
                    statusIcon = "🟡";
                }
                else if (pingMilliseconds < 200)
                {
                    statusColor = new Color(0.9f, 0.7f, 0.3f); // 橙色 - 一般
                    statusText = "Fair";
                    statusIcon = "🟠";
                }
                else
                {
                    statusColor = new Color(0.9f, 0.3f, 0.3f); // 红色 - 较差
                    statusText = "Poor";
                    statusIcon = "🔴";
                }

                // 显示延迟信息
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(new GUIContent("⏱️ Ping (ms)", "Network latency in milliseconds"),
                    GUILayout.Width(EditorGUIUtility.labelWidth - 20));

                GUIStyle pingStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 14,
                    normal = { textColor = statusColor }
                };
                EditorGUILayout.LabelField($"{pingMilliseconds} ms", pingStyle);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(3);

                // 显示秒数格式
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(new GUIContent("⏱️ Ping (s)", "Network latency in seconds"),
                    GUILayout.Width(EditorGUIUtility.labelWidth - 20));

                GUIStyle secondsStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = statusColor }
                };
                EditorGUILayout.LabelField($"{pingSeconds:F3} s", secondsStyle);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(8);

                // 分隔线
                DrawSeparator();
                EditorGUILayout.Space(8);

                // 显示连接状态
                EditorGUILayout.BeginHorizontal();
                GUIStyle statusStyle = new GUIStyle(EditorStyles.label)
                {
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = statusColor }
                };
                EditorGUILayout.LabelField($"{statusIcon} Connection Quality: {statusText}", statusStyle);
                EditorGUILayout.EndHorizontal();

                // 实时刷新
                Repaint();
            }
            catch (System.InvalidOperationException)
            {
                // 心跳组件未初始化
                GUIStyle warningStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.9f, 0.7f, 0.3f) },
                    fontStyle = FontStyle.Italic,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true
                };
                EditorGUILayout.LabelField("⚠ Ping information unavailable - Heartbeat component not initialized or connection not established", warningStyle);
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.EndVertical();
        }

        private void DrawWebSocketOptions()
        {
            // HTTPS Option Box
            Color originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.3f, 0.6f, 0.9f, 0.3f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = originalColor;

            EditorGUILayout.BeginHorizontal();

            // Checkbox with custom label
            GUIStyle toggleStyle = new GUIStyle(EditorStyles.toggle)
            {
                fontStyle = FontStyle.Bold
            };

            enableHttpsProperty.boolValue = EditorGUILayout.Toggle(
                new GUIContent("🔒 Enable HTTPS", "Use secure WebSocket connection (wss://)"),
                enableHttpsProperty.boolValue,
                toggleStyle
            );

            EditorGUILayout.EndHorizontal();

            // Info text
            if (enableHttpsProperty.boolValue)
            {
                EditorGUILayout.Space(3);
                GUIStyle infoStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.5f, 0.8f, 0.5f) },
                    wordWrap = true
                };
                EditorGUILayout.LabelField("✓ Secure connection enabled (wss://)", infoStyle);
            }
            else
            {
                EditorGUILayout.Space(3);
                GUIStyle warningStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.9f, 0.7f, 0.3f) },
                    wordWrap = true
                };
                EditorGUILayout.LabelField("⚠ Using unsecure connection (ws://)", warningStyle);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawRuntimeInstanceSettingsBox()
        {
            Color originalColor = GUI.backgroundColor;
            bool isRuntime = isRuntimeInstanceProperty.boolValue;

            // Change background color based on enabled state
            GUI.backgroundColor = isRuntime ? new Color(0.3f, 0.7f, 0.5f, 0.3f) : new Color(0.5f, 0.5f, 0.5f, 0.3f);

            EditorGUILayout.BeginVertical(boxStyle);
            GUI.backgroundColor = originalColor;

            // Header with toggle
            EditorGUILayout.BeginHorizontal();
            DrawSectionHeader("⚙️ Runtime Instance");
            GUILayout.FlexibleSpace();

            GUIStyle toggleStyle = new GUIStyle(EditorStyles.toggle)
            {
                fontStyle = FontStyle.Bold
            };

            EditorGUILayout.LabelField(new GUIContent("Enable", "Set as global Runtime instance"),
                GUILayout.Width(50));
            isRuntimeInstanceProperty.boolValue = EditorGUILayout.Toggle(isRuntimeInstanceProperty.boolValue, toggleStyle, GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            // Info text
            if (isRuntime)
            {
                GUIStyle infoStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.5f, 0.8f, 0.5f) },
                    wordWrap = true
                };
                EditorGUILayout.LabelField("✓ This instance will be accessible via Runtime.Scene and Runtime.Session", infoStyle);
                EditorGUILayout.Space(3);

                GUIStyle codeStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.7f, 0.9f, 1f) },
                    wordWrap = true,
                    fontStyle = FontStyle.Italic
                };
                EditorGUILayout.LabelField("Example: var scene = Runtime.Scene;", codeStyle);
            }
            else
            {
                GUIStyle disabledStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.6f, 0.6f, 0.6f) },
                    fontStyle = FontStyle.Italic,
                    wordWrap = true
                };
                EditorGUILayout.LabelField("This instance won't be accessible via static Runtime class. Access Scene and Session directly from the component.", disabledStyle);
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.EndVertical();
        }

        private void DrawEventCallbacksBox()
        {
            Color originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.6f, 0.4f, 0.9f, 0.3f);

            EditorGUILayout.BeginVertical(boxStyle);
            GUI.backgroundColor = originalColor;

            // Section Header
            DrawSectionHeader("🎯 Event Callbacks");
            EditorGUILayout.Space(8);

            // Info text
            GUIStyle infoStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) },
                wordWrap = true
            };
            EditorGUILayout.LabelField("Drag and drop methods or assign event handlers below:", infoStyle);
            EditorGUILayout.Space(5);

            // On Connect Complete
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("✅ On Connect Complete", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(onConnectCompleteProperty, GUIContent.none);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);

            // On Connect Fail
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("❌ On Connect Fail", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(onConnectFailProperty, GUIContent.none);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);

            // On Connect Disconnect
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("🔌 On Connect Disconnect", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(onConnectDisconnectProperty, GUIContent.none);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);
            EditorGUILayout.EndVertical();
        }

        private void DrawValidationWarnings()
        {
            bool hasWarnings = false;

            // Validate IP
            if (string.IsNullOrWhiteSpace(remoteIPProperty.stringValue))
            {
                DrawWarningBox("⚠ Remote IP cannot be empty!");
                hasWarnings = true;
            }

            // Validate Port
            if (remotePortProperty.intValue <= 0 || remotePortProperty.intValue > 65535)
            {
                DrawWarningBox("⚠ Port must be between 1 and 65535!");
                hasWarnings = true;
            }

            // Validate Connect Timeout
            if (connectTimeoutProperty.intValue < 1000)
            {
                DrawWarningBox("⚠ Connect timeout should be at least 1000ms!");
                hasWarnings = true;
            }

            // Validate Heartbeat Settings
            if (enableHeartbeatProperty.boolValue)
            {
                if (heartbeatIntervalProperty.intValue < 100)
                {
                    DrawWarningBox("⚠ Heartbeat interval should be at least 100ms!");
                    hasWarnings = true;
                }

                if (heartbeatTimeOutProperty.intValue < heartbeatIntervalProperty.intValue * 2)
                {
                    DrawWarningBox("⚠ Heartbeat timeout should be at least 2x the interval!");
                    hasWarnings = true;
                }

                if (maxPingSamplesProperty.intValue < 1)
                {
                    DrawWarningBox("⚠ Ping samples must be at least 1!");
                    hasWarnings = true;
                }
            }

            // Success message
            if (!hasWarnings)
            {
                DrawSuccessBox("✓ Configuration valid - Ready to connect!");
            }
        }

        private void DrawWarningBox(string message)
        {
            Color originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.5f, 0.3f, 0.3f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = originalColor;

            GUIStyle warningStyle = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.6f, 0.3f) }
            };
            EditorGUILayout.LabelField(message, warningStyle);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }

        private void DrawSuccessBox(string message)
        {
            Color originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f, 0.2f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = originalColor;

            GUIStyle successStyle = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.3f, 0.8f, 0.3f) }
            };
            EditorGUILayout.LabelField(message, successStyle);

            EditorGUILayout.EndVertical();
        }

        private void DrawSectionHeader(string title)
        {
            GUIStyle sectionStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.7f, 0.9f, 1f) }
            };
            EditorGUILayout.LabelField(title, sectionStyle);
        }

        private void DrawSeparator()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 1);
            rect.height = 1;
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
        }

        private string GetProtocolIcon(FantasyRuntime.NetworkProtocolType protocol)
        {
            switch (protocol)
            {
                case FantasyRuntime.NetworkProtocolType.TCP:
                    return "🔗";
                case FantasyRuntime.NetworkProtocolType.KCP:
                    return "⚡";
                case FantasyRuntime.NetworkProtocolType.WebSocket:
                    return "🌐";
                default:
                    return "📡";
            }
        }
    }
}
