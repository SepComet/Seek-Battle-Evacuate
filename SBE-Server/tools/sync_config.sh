#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
SRC="$SCRIPT_DIR/../../SBE-Client/Assets/GameMain/DataTables"
DST="$SCRIPT_DIR/../config"
cp -f "$SRC/tbitemconfig.json" "$DST/tbitemconfig.json"
cp -f "$SRC/tbglobalconfig.json" "$DST/tbglobalconfig.json"
cp -f "$SRC/tbdifficultyconfig.json" "$DST/tbdifficultyconfig.json"
echo "[OK] item/global/difficulty tables synced."
