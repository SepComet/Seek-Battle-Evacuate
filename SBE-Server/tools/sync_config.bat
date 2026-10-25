@echo off
set SRC=%~dp0..\..\SBE-Client\Assets\GameMain\DataTables
set DST=%~dp0..\config
copy /Y "%SRC%\tbitemconfig.json" "%DST%\tbitemconfig.json"
copy /Y "%SRC%\tbglobalconfig.json" "%DST%\tbglobalconfig.json"
copy /Y "%SRC%\tbdifficultyconfig.json" "%DST%\tbdifficultyconfig.json"
rem 探索内容生成所需：资源点掉落权重、敌队伍威胁池、固定地图布局（地图由编辑器菜单导出，非 Luban 产物）
copy /Y "%SRC%\tbresourcepointconfig.json" "%DST%\tbresourcepointconfig.json"
copy /Y "%SRC%\tbenemypartyconfig.json" "%DST%\tbenemypartyconfig.json"
copy /Y "%SRC%\tbmapdefinition.json" "%DST%\tbmapdefinition.json"
echo [OK] item/global/difficulty/resourcepoint/enemyparty/mapdefinition tables synced.
