using System;
using TEngine;
using UnityEngine;

namespace GameLogic
{
    public class ErrorLogger : IDisposable
    {
        private readonly IUIModule _uiModule;
        
        public ErrorLogger( )
        {
            _uiModule = GameModule.UI;
            Application.logMessageReceived += LogHandler;
        }

        public void Dispose()
        {
            Application.logMessageReceived -= LogHandler;
        }

        private void LogHandler(string condition, string stacktrace, LogType type)
        {
            if (type == LogType.Exception)
            {
                string des = $"客户端报错, \n#内容#：---{condition} \n#位置#：---{stacktrace}";
                _uiModule.ShowUIAsync<LogUI>(des);
            }
        }
    }
}