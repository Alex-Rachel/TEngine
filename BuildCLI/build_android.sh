#!/bin/bash

cd "$(dirname "$0")"

source ./path_define.sh

"${UNITYEDITOR_PATH}/Unity" "${WORKSPACE}" \
  -logFile "${BUILD_LOGFILE}" \
  -executeMethod TEngine.ReleaseTools.AutomationBuildAndroid \
  -quit -batchmode \
  -CustomArgs:Language=en_US "${WORKSPACE}"

while IFS= read -r line; do
  echo "$line"
done < "${BUILD_LOGFILE}"

echo "按任意键继续..."
read -k1
