using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking.PlayerConnection;

namespace YooAsset
{
    internal class RemoteDebuggerInRuntime : MonoBehaviour
    {
#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnRuntimeInitialize()
        {
            _sampleOnce = false;
            _autoSample = false;
        }
#endif

        private static bool _sampleOnce = false;
        private static bool _autoSample = false;

        private void Awake()
        {
#if UNITY_EDITOR
            RemotePlayerConnection.Instance.Initialize();
#endif
        }
        private void OnEnable()
        {
#if UNITY_EDITOR
            RemotePlayerConnection.Instance.Register(RemoteDebuggerDefine.kMsgEditorSendToPlayer, OnHandleEditorMessage);
#else
            PlayerConnection.instance.Register(RemoteDebuggerDefine.kMsgEditorSendToPlayer, OnHandleEditorMessage);
#endif
        }
        private void OnDisable()
        {
#if UNITY_EDITOR
            RemotePlayerConnection.Instance.Unregister(RemoteDebuggerDefine.kMsgEditorSendToPlayer);
#else
            PlayerConnection.instance.Unregister(RemoteDebuggerDefine.kMsgEditorSendToPlayer, OnHandleEditorMessage);
#endif
        }
        private void LateUpdate()
        {
            if (_autoSample || _sampleOnce)
            {
                _sampleOnce = false;
                var debugReport = YooAssets.GetDebugReport();
                var data = DebugReport.Serialize(debugReport);

#if UNITY_EDITOR
                RemotePlayerConnection.Instance.Send(RemoteDebuggerDefine.kMsgPlayerSendToEditor, data);
#else
                PlayerConnection.instance.Send(RemoteDebuggerDefine.kMsgPlayerSendToEditor, data);
#endif
            }
        }

        private static void OnHandleEditorMessage(MessageEventArgs args)
        {
            var command = RemoteCommand.Deserialize(args.data);
            YooLogger.Log($"On handle remote command : {command.CommandType} Param : {command.CommandParam}");
            if (command.CommandType == (int)ERemoteCommand.SampleOnce)
            {
                _sampleOnce = true;
            }
            else if (command.CommandType == (int)ERemoteCommand.SampleAuto)
            {
                if (command.CommandParam == "open")
                    _autoSample = true;
                else
                    _autoSample = false;
            }
            else
            {
                throw new NotImplementedException(command.CommandType.ToString());
            }
        }
    }
}