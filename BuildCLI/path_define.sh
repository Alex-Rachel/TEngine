#!/bin/bash

cd "$(dirname "$0")"

export WORKSPACE="/Users/your_user/github/TEngine/UnityProject"  # 请替换为 macOS 上的实际路径
export UNITYEDITOR_PATH="/Applications/Unity/Hub/Editor/2021.3.20f1c1/Unity.app/Contents/MacOS"  # 请替换为 macOS 上的 Unity 路径
export BUILD_DLL_LOGFILE="./build_dll.log"
export BUILD_LOGFILE="./build.log"

echo "环境变量已设置："
echo "WORKSPACE=${WORKSPACE}"
echo "UNITYEDITOR_PATH=${UNITYEDITOR_PATH}"
echo "BUILD_DLL_LOGFILE=${BUILD_DLL_LOGFILE}"
echo "BUILD_LOGFILE=${BUILD_LOGFILE}"
