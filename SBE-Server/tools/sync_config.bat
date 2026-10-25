@echo off
copy /Y "%~dp0..\..\SBE-Client\Assets\GameMain\DataTables	bitemconfig.json" "%~dp0..\config	bitemconfig.json"
echo [OK] item table synced.
