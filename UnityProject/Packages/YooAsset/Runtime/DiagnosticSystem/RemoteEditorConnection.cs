using System;
using System.Collections.Generic;
using UnityEngine.Events;
using UnityEngine.Networking.PlayerConnection;
using UnityEngine;

namespace YooAsset
{
    internal class RemoteEditorConnection
    {
#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnRuntimeInitialize()
        {
            _instance = null;
        }
#endif

        private static RemoteEditorConnection _instance;
        public static RemoteEditorConnection Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new RemoteEditorConnection();
                return _instance;
            }
        }

        private readonly Dictionary<Guid, UnityAction<MessageEventArgs>> _messageCallbacks = new Dictionary<Guid, UnityAction<MessageEventArgs>>();

        public void Initialize()
        {
            _messageCallbacks.Clear();
        }
        public void Register(Guid messageID, UnityAction<MessageEventArgs> callback)
        {
            if (messageID == Guid.Empty)
                throw new ArgumentException("messageID is empty !");

            if (_messageCallbacks.ContainsKey(messageID) == false)
                _messageCallbacks.Add(messageID, callback);
        }
        public void Unregister(Guid messageID)
        {
            if (_messageCallbacks.ContainsKey(messageID))
                _messageCallbacks.Remove(messageID);
        }
        public void Send(Guid messageID, byte[] data)
        {
            if (messageID == Guid.Empty)
                throw new ArgumentException("messageID is empty !");

            // 接收对方的消息
            RemotePlayerConnection.Instance.HandleEditorMessage(messageID, data);
        }

        internal void HandlePlayerMessage(Guid messageID, byte[] data)
        {
            if (_messageCallbacks.TryGetValue(messageID, out UnityAction<MessageEventArgs> value))
            {
                var args = new MessageEventArgs();
                args.playerId = 0;
                args.data = data;
                value?.Invoke(args);
            }
        }
    }
}