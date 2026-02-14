using Fantasy;
using Fantasy.Network.Interface;

namespace GameLogic
{
    public class LoginNetMgr : DataCenterModule<LoginNetMgr>
    {
        public override void OnInit()
        {
            GameClient.Instance.RegisterMsgHandler(OuterOpcode.G2C_LoginMessage, OnLoginMessageNotify);
        }

        private void OnLoginMessageNotify(IMessage obj)
        {
            if (obj == null || obj is not G2C_LoginMessage msg)
            {
                return;
            }
            Log.Info($"登录成功，服务器下发消息: {msg.Msg}");
        }
    }
}