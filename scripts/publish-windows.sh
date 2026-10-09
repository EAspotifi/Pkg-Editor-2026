#!/usr/bin/env bash
# Publish self-contained Windows x64 single-file (Avalonia).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-$ROOT/publish-win}"

mkdir -p "$OUT"
dotnet publish "$ROOT/PkgEditorLinux/PkgEditorLinux.csproj" \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -o "$OUT" \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -p:NuGetAudit=false

# Drop leftover satellite/config noise if any (keep exe + pdb-less publish)
find "$OUT" -maxdepth 1 -type f ! -name 'PkgEditor.exe' ! -name '*.dll' ! -name '*.json' ! -name '*.pdb' -delete 2>/dev/null || true

echo "Windows publish listo: $OUT"
ls -lh "$OUT" | head -20
