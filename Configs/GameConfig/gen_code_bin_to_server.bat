Cd /d %~dp0
echo %CD%

set WORKSPACE=../../
set LUBAN_DLL=%WORKSPACE%/Tools/Luban/Luban.dll
set CONF_ROOT=.
set DATA_OUTPATH=%WORKSPACE%/GameServer/GameConfig/Binary
set CODE_OUTPATH=%WORKSPACE%/GameServer/Server/Entity/Generate/GameConfig

xcopy /s /e /i /y "%CONF_ROOT%\CustomTemplate\ServerConfigSystem.cs" "%WORKSPACE%\GameServer\Server\Entity\Generate\ServerConfigSystem.cs"

dotnet %LUBAN_DLL% ^
    -t server^
    -c cs-bin ^
    -d bin^
    --conf %CONF_ROOT%\luban.conf ^
    -x code.lineEnding=crlf ^
    -x outputCodeDir=%CODE_OUTPATH% ^
    -x outputDataDir=%DATA_OUTPATH% ^
    -x outputSaver.bin.cleanUpOutputDir=1 ^
    -x outputSaver.json.cleanUpOutputDir=1 ^
    -x outputSaver.cs-bin.cleanUpOutputDir=1 
pause

