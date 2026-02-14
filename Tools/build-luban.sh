#!/bin/bash

[ -d Luban ] && rm -rf Luban

dotnet build  ../../luban/src/Luban/Luban.csproj -c Release -o Luban