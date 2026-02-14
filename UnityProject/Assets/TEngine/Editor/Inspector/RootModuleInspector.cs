using System;
using TEngine;
using System.Collections.Generic;
using TEngine.Editor.Inspector;
using UnityEditor;
using UnityEngine;

namespace TEngine.Editor
{
    [CustomEditor(typeof(RootModule))]
    internal sealed class RootModuleInspector : GameFrameworkInspector
    {
        private const string NONE_OPTION_NAME = "<None>";
        private static readonly float[] GameSpeed = new float[] { 0f, 0.01f, 0.1f, 0.25f, 0.5f, 1f, 1.5f, 2f, 4f, 8f };
        private static readonly string[] GameSpeedForDisplay = new string[] { "0x", "0.01x", "0.1x", "0.25x", "0.5x", "1x", "1.5x", "2x", "4x", "8x" };

        private SerializedProperty _editorLanguage = null;
        private SerializedProperty _textHelperTypeName = null;
        private SerializedProperty _logHelperTypeName = null;
        private SerializedProperty _jsonHelperTypeName = null;
        private SerializedProperty _frameRate = null;
        private SerializedProperty _gameSpeed = null;
        private SerializedProperty _runInBackground = null;
        private SerializedProperty _neverSleep = null;

        private string[] _textHelperTypeNames = null;
        private int _textHelperTypeNameIndex = 0;
        private string[] _logHelperTypeNames = null;
        private int _logHelperTypeNameIndex = 0;
        private string[] _jsonHelperTypeNames = null;
        private int _jsonHelperTypeNameIndex = 0;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            serializedObject.Update();

            RootModule t = (RootModule)target;

            EditorGUI.BeginDisabledGroup(EditorApplication.isPlayingOrWillChangePlaymode);
            {
                EditorGUILayout.PropertyField(_editorLanguage);
                
                EditorGUILayout.BeginVertical("box");
                {
                    EditorGUILayout.LabelField("Global Helpers", EditorStyles.boldLabel);

                    int textHelperSelectedIndex = EditorGUILayout.Popup("Text Helper", _textHelperTypeNameIndex, _textHelperTypeNames);
                    if (textHelperSelectedIndex != _textHelperTypeNameIndex)
                    {
                        _textHelperTypeNameIndex = textHelperSelectedIndex;
                        _textHelperTypeName.stringValue = textHelperSelectedIndex <= 0 ? null : _textHelperTypeNames[textHelperSelectedIndex];
                    }

                    int logHelperSelectedIndex = EditorGUILayout.Popup("Log Helper", _logHelperTypeNameIndex, _logHelperTypeNames);
                    if (logHelperSelectedIndex != _logHelperTypeNameIndex)
                    {
                        _logHelperTypeNameIndex = logHelperSelectedIndex;
                        _logHelperTypeName.stringValue = logHelperSelectedIndex <= 0 ? null : _logHelperTypeNames[logHelperSelectedIndex];
                    }

                    int jsonHelperSelectedIndex = EditorGUILayout.Popup("JSON Helper", _jsonHelperTypeNameIndex, _jsonHelperTypeNames);
                    if (jsonHelperSelectedIndex != _jsonHelperTypeNameIndex)
                    {
                        _jsonHelperTypeNameIndex = jsonHelperSelectedIndex;
                        _jsonHelperTypeName.stringValue = jsonHelperSelectedIndex <= 0 ? null : _jsonHelperTypeNames[jsonHelperSelectedIndex];
                    }
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUI.EndDisabledGroup();

            int frameRate = EditorGUILayout.IntSlider("Frame Rate", _frameRate.intValue, 1, 120);
            if (frameRate != _frameRate.intValue)
            {
                if (EditorApplication.isPlaying)
                {
                    t.FrameRate = frameRate;
                }
                else
                {
                    _frameRate.intValue = frameRate;
                }
            }

            EditorGUILayout.BeginVertical("box");
            {
                float gameSpeed = EditorGUILayout.Slider("Game Speed", _gameSpeed.floatValue, 0f, 8f);
                int selectedGameSpeed = GUILayout.SelectionGrid(GetSelectedGameSpeed(gameSpeed), GameSpeedForDisplay, 5);
                if (selectedGameSpeed >= 0)
                {
                    gameSpeed = GetGameSpeed(selectedGameSpeed);
                }

                if (Math.Abs(gameSpeed - _gameSpeed.floatValue) > 0.01f)
                {
                    if (EditorApplication.isPlaying)
                    {
                        t.GameSpeed = gameSpeed;
                    }
                    else
                    {
                        _gameSpeed.floatValue = gameSpeed;
                    }
                }
            }
            EditorGUILayout.EndVertical();

            bool runInBackground = EditorGUILayout.Toggle("Run in Background", _runInBackground.boolValue);
            if (runInBackground != _runInBackground.boolValue)
            {
                if (EditorApplication.isPlaying)
                {
                    t.RunInBackground = runInBackground;
                }
                else
                {
                    _runInBackground.boolValue = runInBackground;
                }
            }

            bool neverSleep = EditorGUILayout.Toggle("Never Sleep", _neverSleep.boolValue);
            if (neverSleep != _neverSleep.boolValue)
            {
                if (EditorApplication.isPlaying)
                {
                    t.NeverSleep = neverSleep;
                }
                else
                {
                    _neverSleep.boolValue = neverSleep;
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        protected override void OnCompileComplete()
        {
            base.OnCompileComplete();

            RefreshTypeNames();
        }

        private void OnEnable()
        {
            _editorLanguage = serializedObject.FindProperty("editorLanguage");
            _textHelperTypeName = serializedObject.FindProperty("textHelperTypeName");
            _logHelperTypeName = serializedObject.FindProperty("logHelperTypeName"); 
            _jsonHelperTypeName = serializedObject.FindProperty("jsonHelperTypeName");
            _frameRate = serializedObject.FindProperty("frameRate");
            _gameSpeed = serializedObject.FindProperty("gameSpeed");
            _runInBackground = serializedObject.FindProperty("runInBackground");
            _neverSleep = serializedObject.FindProperty("neverSleep");

            RefreshTypeNames();
        }

        private void RefreshTypeNames()
        {
            List<string> textHelperTypeNames = new List<string>
            {
                NONE_OPTION_NAME
            };

            textHelperTypeNames.AddRange(Type.GetRuntimeTypeNames(typeof(Utility.Text.ITextHelper)));
            _textHelperTypeNames = textHelperTypeNames.ToArray();
            _textHelperTypeNameIndex = 0;
            if (!string.IsNullOrEmpty(_textHelperTypeName.stringValue))
            {
                _textHelperTypeNameIndex = textHelperTypeNames.IndexOf(_textHelperTypeName.stringValue);
                if (_textHelperTypeNameIndex <= 0)
                {
                    _textHelperTypeNameIndex = 0;
                    _textHelperTypeName.stringValue = null;
                }
            }

            List<string> logHelperTypeNames = new List<string>
            {
                NONE_OPTION_NAME
            };

            logHelperTypeNames.AddRange(Type.GetRuntimeTypeNames(typeof(GameFrameworkLog.ILogHelper)));
            _logHelperTypeNames = logHelperTypeNames.ToArray();
            _logHelperTypeNameIndex = 0;
            if (!string.IsNullOrEmpty(_logHelperTypeName.stringValue))
            {
                _logHelperTypeNameIndex = logHelperTypeNames.IndexOf(_logHelperTypeName.stringValue);
                if (_logHelperTypeNameIndex <= 0)
                {
                    _logHelperTypeNameIndex = 0;
                    _logHelperTypeName.stringValue = null;
                }
            }

            List<string> jsonHelperTypeNames = new List<string>
            {
                NONE_OPTION_NAME
            };

            jsonHelperTypeNames.AddRange(Type.GetRuntimeTypeNames(typeof(Utility.Json.IJsonHelper)));
            _jsonHelperTypeNames = jsonHelperTypeNames.ToArray();
            _jsonHelperTypeNameIndex = 0;
            if (!string.IsNullOrEmpty(_jsonHelperTypeName.stringValue))
            {
                _jsonHelperTypeNameIndex = jsonHelperTypeNames.IndexOf(_jsonHelperTypeName.stringValue);
                if (_jsonHelperTypeNameIndex <= 0)
                {
                    _jsonHelperTypeNameIndex = 0;
                    _jsonHelperTypeName.stringValue = null;
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private float GetGameSpeed(int selectedGameSpeed)
        {
            if (selectedGameSpeed < 0)
            {
                return GameSpeed[0];
            }

            if (selectedGameSpeed >= GameSpeed.Length)
            {
                return GameSpeed[GameSpeed.Length - 1];
            }

            return GameSpeed[selectedGameSpeed];
        }

        private int GetSelectedGameSpeed(float gameSpeed)
        {
            for (int i = 0; i < GameSpeed.Length; i++)
            {
                if (Mathf.Approximately(gameSpeed, GameSpeed[i]))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
