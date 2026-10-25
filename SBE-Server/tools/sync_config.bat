@echo off
set SRC=%~dp0..\..\SBE-Client\Assets\GameMain\DataTables
set DST=%~dp0..\config
copy /Y "%SRC%\tbitemconfig.json" "%DST%\tbitemconfig.json"
copy /Y "%SRC%\tbglobalconfig.json" "%DST%\tbglobalconfig.json"
copy /Y "%SRC%\tbdifficultyconfig.json" "%DST%\tbdifficultyconfig.json"
echo [OK] item/global/difficulty tables synced.
