cd /d %~dp0

call path_define.bat

%UNITYEDITOR_PATH%/Unity.exe %WORKSPACE% -logFile %BUILD_LOGFILE% -executeMethod TEngine.ReleaseTools.AutomationBuildAndroid -quit -batchmode -CustomArgs:Language=en_US; %WORKSPACE%

@REM for /f "delims=[" %%i in (%BUILD_LOGFILE%) do echo %%i

pause