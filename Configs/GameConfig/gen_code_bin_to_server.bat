Cd /d %~dp0
echo %CD%

set WORKSPACE=../../
set LUBAN_DLL=%WORKSPACE%/Tools/Luban/Luban.dll
set CONF_ROOT=.
set DATA_OUTPATH=%WORKSPACE%/Server/GameConfig 
set CODE_OUTPATH=%WORKSPACE%/Server/Hotfix/Config/GameConfig

dotnet %LUBAN_DLL% ^
    -t server^
    -c cs-bin ^
    -d bin^
    --conf %CONF_ROOT%\luban.conf ^
    -x code.lineEnding=crlf ^
    -x outputCodeDir=%CODE_OUTPATH% ^
    -x outputDataDir=%DATA_OUTPATH% 
pause

