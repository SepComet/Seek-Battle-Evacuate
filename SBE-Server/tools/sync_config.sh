#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
SRC="$SCRIPT_DIR/../../SBE-Client/Assets/GameMain/DataTables"
DST="$SCRIPT_DIR/../config"
cp -f "$SRC/tbitemconfig.json" "$DST/tbitemconfig.json"
cp -f "$SRC/tbglobalconfig.json" "$DST/tbglobalconfig.json"
cp -f "$SRC/tbdifficultyconfig.json" "$DST/tbdifficultyconfig.json"
# 探索内容生成所需：资源点掉落权重、敌队伍威胁池、固定地图布局（地图由编辑器菜单导出，非 Luban 产物）
cp -f "$SRC/tbresourcepointconfig.json" "$DST/tbresourcepointconfig.json"
cp -f "$SRC/tbenemypartyconfig.json" "$DST/tbenemypartyconfig.json"
cp -f "$SRC/tbmapdefinition.json" "$DST/tbmapdefinition.json"
echo "[OK] item/global/difficulty/resourcepoint/enemyparty/mapdefinition tables synced."
