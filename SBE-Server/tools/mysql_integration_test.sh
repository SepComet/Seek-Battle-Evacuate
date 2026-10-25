#!/usr/bin/env bash
# 一键 MySQL 集成测试：重建一次性测试库 sbe_integration_test 并运行 mysqlstore 集成用例。
# 用法：tools/mysql_integration_test.sh -user root -pass secret [-host 127.0.0.1] [-port 3306]
# 参数原样传给 cmd/mysqltest；测试库每次运行清空重建，绝不用于业务数据。

set -euo pipefail
cd "$(dirname "$0")/.."
exec go run ./cmd/mysqltest "$@"
