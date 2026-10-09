#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/Portable"

mkdir -p "$OUT"
dotnet publish "$ROOT/PkgEditorLinux/PkgEditorLinux.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -o "$OUT" \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -p:NuGetAudit=false

chmod +x "$OUT/PkgEditor"
# Keep docs; remove publish leftovers if any
find "$OUT" -maxdepth 1 -type f ! -name 'PkgEditor' ! -name 'README.md' ! -name 'Launch.sh' ! -name '*.desktop' -delete 2>/dev/null || true
[ -x "$OUT/Launch.sh" ] || printf '%s\n' '#!/usr/bin/env bash' 'cd "$(dirname "$(readlink -f "$0")")"' 'exec ./PkgEditor "$@"' > "$OUT/Launch.sh" && chmod +x "$OUT/Launch.sh"

echo "Portable listo: $OUT/PkgEditor"
ls -lh "$OUT/PkgEditor"
