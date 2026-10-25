#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
cp -f "$SCRIPT_DIR/../../SBE-Client/Assets/GameMain/DataTables/tbitemconfig.json" "$SCRIPT_DIR/../config/tbitemconfig.json"
echo "[OK] item table synced."
