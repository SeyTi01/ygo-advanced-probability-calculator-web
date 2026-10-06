#!/bin/sh
curl -sSL https://dot.net/v1/dotnet-install.sh > dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh -c 10.0 -InstallDir ./dotnet
./dotnet/dotnet --version
./dotnet/dotnet workload install wasm-tools
./dotnet/dotnet workload list
./dotnet/dotnet publish -c Release -o output
