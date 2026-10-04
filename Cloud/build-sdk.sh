#!/usr/bin/env bash
# Builds the NodeSDK for linux-x64 inside WSL (CentOS7) and copies the output to
# Cloud/sdk. Run from Windows with:  npm run build:sdk
#
# WSL one-time setup (CentOS7, glibc 2.17):
#   1. .NET 8 SDK:      curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir /root/.dotnet8
#   2. libstdc++ 11:    build GCC 11.2 libstdc++ from source into /root/stdc11/lib
#                       (dotnet needs GLIBCXX_3.4.20+, CentOS7 ships 3.4.19)
#   3. ilc fix:         copy ilc's libc++abi.so.1/libc++.so.1/libobjwriter.so to
#                       /root/ilcfix, patch libc++abi's GLIBC_2.18 verneed to
#                       GLIBC_2.17 (string + hash + versym), add objwriter.so name
#                       shim, and install the ilc wrapper (ilc -> ilc.real + env).
#   4. devtoolset-11:   yum install -y centos-release-scl devtoolset-11-gcc devtoolset-11-gcc-c++
#                       (fix SCLo repos to vault.centos.org on CentOS 7 EOL)
set -eu
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_ROOT=/root/.dotnet8
export PATH="/opt/rh/devtoolset-11/root/usr/bin:/root/.dotnet8:$PATH"
export LD_LIBRARY_PATH=/root/ilcfix:/root/stdc11/lib
export LD_PRELOAD=/root/ilcfix/libcxa17.so

REPO=/mnt/d/Projects/Atool/Xvirus/AntiMalwareSDK/projeto/XescSDK
NODESDK="$REPO/NodeSDK"
SDK_OUT="$REPO/Cloud/sdk"

# gcc as linker + no symbol compression: CentOS7's clang/ld are too old for -gz=zlib.
cat > /tmp/centos7.props << 'EOF'
<Project>
  <PropertyGroup>
    <CompressSymbols>false</CompressSymbols>
    <CppCompilerAndLinker>gcc</CppCompilerAndLinker>
    <CppLinker>gcc</CppLinker>
  </PropertyGroup>
</Project>
EOF

echo "=== publish linux-x64 ==="
cd "$NODESDK"
dotnet publish -r linux-x64 -c Release -p:CustomAfterMicrosoftCommonTargets=/tmp/centos7.props

echo "=== copy outputs to Cloud/sdk ==="
PUB="$NODESDK/bin/Release/net8.0/linux-x64/publish"
cp -f "$PUB/XvirusNodeSDK.node" "$SDK_OUT/"
cp -f "$PUB/XvirusNodeSDK.mjs" "$SDK_OUT/"
cp -f "$PUB/import.cjs" "$SDK_OUT/"
# Root .so files: ONNX Runtime, loaded from next to the .node module.
for so in "$PUB"/*.so; do
  [ -f "$so" ] && cp -f "$so" "$SDK_OUT/"
done
# LLamaSharp resolves its llama.cpp backend via the relative path
# runtimes/linux-x64/native/<avx-variant>/libllama.so (plus libggml*/libmtmd
# dependencies from the same tree), searched from the SDK base folder — copy it
# with the directory layout intact or the script AI engine cannot load.
mkdir -p "$SDK_OUT/runtimes"
rm -rf "$SDK_OUT/runtimes/linux-x64"
cp -rf "$PUB/runtimes/linux-x64" "$SDK_OUT/runtimes/"

echo "=== done ==="
ls -la "$SDK_OUT"
ls -la "$SDK_OUT/runtimes/linux-x64/native"
