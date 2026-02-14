#region Class Documentation
/************************************************************************************************************
Class Name:     CommandLineReader.cs
Type:           Util, Static
Definition:
                CommandLineReader.cs give the ability to access [Custom Arguments] sent 
                through the command line. Simply add your custom arguments under the
                keyword '-CustomArgs:' and seperate them by ';'.
Example:
                C:\Program Files (x86)\Unity\Editor\Unity.exe [ProjectLocation] -executeMethod [Your entrypoint] -quit -CustomArgs:Language=en_US;Version=1.02
                
Example1:
                set WORKSPACE=.
                set UNITYEDITOR_PATH=G:/UnityEditor/2021.3.20f1c1/Editor
                set LOGFILE=./build.log
                set BUILDROOT=G:/github/TEngine/UnityProject/Bundles

                %UNITYEDITOR_PATH%/Unity.exe %WORKSPACE%/UnityProject -logFile %LOGFILE% -executeMethod TEngine.ReleaseTools.BuildPackage -quit -batchmode -CustomArgs:Language=en_US;Version=1.02;outputRoot=%BUILDROOT%

                @REM for /f "delims=[" %%i in (%LOGFILE%) do echo %%i

                pause
************************************************************************************************************/
#endregion

#region Using
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using Debug = UnityEngine.Debug;
#endregion

/// <summary>
/// Unity命令行拓展帮助类。
/// <remarks>可以用来制定自己项目的打包、编辑器工作流。</remarks>
/// </summary>
public class CommandLineReader
{
    //Config
    private const string CUSTOM_ARGS_PREFIX = "-CustomArgs:";
    private const char CUSTOM_ARGS_SEPARATOR = ';';

    public static string[] GetCommandLineArgs()
    {
        return Environment.GetCommandLineArgs();
    }

    public static string GetCommandLine()
    {
        string[] args = GetCommandLineArgs();

        if (args.Length > 0)
        {
            return string.Join(" ", args);
        }
        else
        {
            Debug.LogError("CommandLineReader.cs - GetCommandLine() - Can't find any command line arguments!");
            return "";
        }
    }

    public static Dictionary<string,string> GetCustomArguments()
    {
        Dictionary<string, string> customArgsDict = new Dictionary<string, string>();
        string[] commandLineArgs = GetCommandLineArgs();
        string[] customArgs;
        string[] customArgBuffer;
        string customArgsStr = "";
        
        try
        {
            customArgsStr = commandLineArgs.Where(row => row.Contains(CUSTOM_ARGS_PREFIX)).Single();
        }
        catch (Exception e)
        {
            Debug.LogError("CommandLineReader.cs - GetCustomArguments() - Can't retrieve any custom arguments in the command line [" + commandLineArgs + "]. Exception: " + e);
            return customArgsDict;
        }

        customArgsStr = customArgsStr.Replace(CUSTOM_ARGS_PREFIX, "");
        customArgs = customArgsStr.Split(CUSTOM_ARGS_SEPARATOR);

        foreach (string customArg in customArgs)
        {
            customArgBuffer = customArg.Split('=');
            if (customArgBuffer.Length == 2)
            {
                customArgsDict.Add(customArgBuffer[0], customArgBuffer[1]);
            }
            else
            {
                Debug.LogWarning("CommandLineReader.cs - GetCustomArguments() - The custom argument [" + customArg + "] seem to be malformed.");
            }
        }

        return customArgsDict;
    }

    /// <summary>
    /// 获取cmd输入的自定义参数数值。
    /// </summary>
    /// <param name="argumentName">自定义参数名称。</param>
    /// <returns>自定义参数数值。</returns>
    public static string GetCustomArgument(string argumentName)
    {
        Dictionary<string, string> customArgsDict = GetCustomArguments();

        if (customArgsDict.TryGetValue(argumentName, out var argument))
        {
            return argument;
        }
        else
        {
            Debug.LogError("CommandLineReader.cs - GetCustomArgument() - Can't retrieve any custom argument named [" + argumentName + "] in the command line [" + GetCommandLine() + "].");
            return "";
        }
    }
}
