#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
namespace Fantasy.Network.KCP
{
    internal enum KcpHeader : byte
    {
        None = 0x00,
        RequestConnection = 0x01,
        WaitConfirmConnection = 0x02,
        ConfirmConnection = 0x03,
        RepeatChannelId = 0x04,
        ReceiveData = 0x06,
        Disconnect = 0x07
    }
}