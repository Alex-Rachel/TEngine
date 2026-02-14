using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;

namespace TEngine.Editor
{
    /// <summary>
    /// Unity编辑器主动执行cmd帮助类。
    /// </summary>
    public static class ShellHelper
    {
        public static void Run(string cmd, string workDirectory, List<string> environmentVars = null)
        {
            System.Diagnostics.Process process = new();
            try
            {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
                string app = "bash";
                string splitChar = ":";
                string arguments = "-c";
#elif UNITY_EDITOR_WIN
                string app = "cmd.exe";
                string splitChar = ";";
                string arguments = "/c";
#endif
                ProcessStartInfo start = new ProcessStartInfo(app);

                if (environmentVars != null)
                {
                    foreach (string var in environmentVars)
                    {
                        start.EnvironmentVariables["PATH"] += (splitChar + var);
                    }
                }

                process.StartInfo = start;
                start.Arguments = arguments + " \"" + cmd + "\"";
                start.CreateNoWindow = true;
                start.ErrorDialog = true;
                start.UseShellExecute = false;
                start.WorkingDirectory = workDirectory;

                if (start.UseShellExecute)
                {
                    start.RedirectStandardOutput = false;
                    start.RedirectStandardError = false;
                    start.RedirectStandardInput = false;
                }
                else
                {
                    start.RedirectStandardOutput = true;
                    start.RedirectStandardError = true;
                    start.RedirectStandardInput = true;
                    start.StandardOutputEncoding = System.Text.Encoding.UTF8;
                    start.StandardErrorEncoding = System.Text.Encoding.UTF8;
                }

                bool endOutput = false;
                bool endError = false;

                process.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        UnityEngine.Debug.Log(args.Data);
                    }
                    else
                    {
                        endOutput = true;
                    }
                };

                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        UnityEngine.Debug.LogError(args.Data);
                    }
                    else
                    {
                        endError = true;
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                while (!endOutput || !endError)
                {
                }

                process.CancelOutputRead();
                process.CancelErrorRead();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
            finally
            {
                process.Close();
            }
        }

        public static void RunByPath(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    Process process = new Process();
                    ProcessStartInfo startInfo = new ProcessStartInfo(path)
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        // 添加编码设置，确保中文正确显示
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    process.StartInfo = startInfo;
                    process.OutputDataReceived += (_, args) =>
                    {
                        if (args.Data != null)
                        {
                            UnityEngine.Debug.Log($"[Process Output]: {args.Data}");
                        }
                    };
                    process.ErrorDataReceived += (_, args) =>
                    {
                        if (args.Data != null)
                        {
                            UnityEngine.Debug.LogError($"[Process Error]: {args.Data}");
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    UnityEngine.Debug.Log($"Started process with ID: {process.Id} for path: {path}");
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"Error starting process at path {path}: {e.Message}");
                    UnityEngine.Debug.LogException(e);
                }
            }
        }
    }
}