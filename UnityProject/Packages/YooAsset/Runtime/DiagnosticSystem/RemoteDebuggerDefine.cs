using System;

namespace YooAsset
{
    internal class RemoteDebuggerDefine
    {
        public const string DebuggerVersion = "2.3.3";
        public static readonly Guid kMsgPlayerSendToEditor = new Guid("e34a5702dd353724aa315fb8011f08c3");
        public static readonly Guid kMsgEditorSendToPlayer = new Guid("4d1926c9df5b052469a1c63448b7609a");
    }
}