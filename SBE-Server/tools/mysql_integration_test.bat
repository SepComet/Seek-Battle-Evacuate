@echo off
setlocal EnableExtensions

rem 一键 MySQL 集成测试：重建一次性测试库 sbe_integration_test 并运行 mysqlstore 集成用例。
rem 用法：tools\mysql_integration_test.bat -user root -pass secret [-host 127.0.0.1] [-port 3306]
rem 参数原样传给 cmd/mysqltest；测试库每次运行清空重建，绝不用于业务数据。

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%.." || exit /b 1

go run ./cmd/mysqltest %*
set "EXIT_CODE=%ERRORLEVEL%"
popd
exit /b %EXIT_CODE%
