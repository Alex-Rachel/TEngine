#!/bin/bash

cd "$(dirname "$0")"
echo "当前目录: $(pwd)"

export WORKSPACE="$(realpath ../../)"
export LUBAN_DLL="${WORKSPACE}/Tools/Luban/Luban.dll"
export CONF_ROOT="$(pwd)"
export DATA_OUTPATH="${WORKSPACE}/UnityProject/Assets/AssetRaw/Configs/bytes/"
export CODE_OUTPATH="${WORKSPACE}/UnityProject/Assets/GameScripts/HotFix/GameProto/GameConfig/"

cp -R "${CONF_ROOT}/CustomTemplate/ConfigSystem.cs" \
   "${WORKSPACE}/UnityProject/Assets/GameScripts/HotFix/GameProto/ConfigSystem.cs"
cp -R "${CONF_ROOT}/CustomTemplate/ExternalTypeUtil.cs" \
    "${WORKSPACE}/UnityProject/Assets/GameScripts/HotFix/GameProto/ExternalTypeUtil.cs"

dotnet "${LUBAN_DLL}" \
    -t client \
    -c cs-bin \
    -d bin \
    --conf "${CONF_ROOT}/luban.conf" \
    -x code.lineEnding=crlf \
    -x outputCodeDir="${CODE_OUTPATH}" \
    -x outputDataDir="${DATA_OUTPATH}"
