@echo off
setlocal EnableExtensions

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%" >nul || exit /b 1

set "PATH_FILE=%SCRIPT_DIR%path.txt"
set "LUBAN_DLL=%SCRIPT_DIR%Tools\Luban\Luban.dll"
set "CONF_FILE=%SCRIPT_DIR%luban.conf"
set "CUSTOM_TEMPLATE_DIR=%SCRIPT_DIR%CustomTemplates"

if not exist "%PATH_FILE%" (
    echo [ERROR] Missing output path file: %PATH_FILE%
    goto :ON_ERROR
)

if not exist "%LUBAN_DLL%" (
    echo [ERROR] Missing Luban executable: %LUBAN_DLL%
    goto :ON_ERROR
)

set /p "path_content="<"%PATH_FILE%"
if not defined path_content (
    echo [ERROR] path.txt must contain a non-empty output directory.
    goto :ON_ERROR
)

if defined LUBAN_OUTPUT_ROOT (
    set "output_root_input=%LUBAN_OUTPUT_ROOT%"
) else (
    set "output_root_input=%path_content%"
)

for %%I in ("%SCRIPT_DIR%..") do set "PROJECT_ROOT=%%~fI"
for %%I in ("%output_root_input%") do set "OUTPUT_ROOT=%%~fI"

set "ASSET_ROOT=%PROJECT_ROOT%\Assets\GameMain\Textures"
set "PATH_DATA_ROOT=%OUTPUT_ROOT%\DataTables"
set "PATH_GEN_CSHARP=%OUTPUT_ROOT%\Scripts\Base\Gen"
set "PATH_DATA_JSON=%PATH_DATA_ROOT%"
set "PATH_DATA_BIN=%PATH_DATA_ROOT%"

if not exist "%PATH_GEN_CSHARP%" mkdir "%PATH_GEN_CSHARP%"
if not exist "%PATH_DATA_ROOT%" mkdir "%PATH_DATA_ROOT%"

echo [INFO] Project root: %PROJECT_ROOT%
echo [INFO] C# output:   %PATH_GEN_CSHARP%
echo [INFO] JSON output: %PATH_DATA_JSON%
echo [INFO] Bin output:  %PATH_DATA_BIN%

rem Gen contains only generated C# files, so Luban may clean stale outputs.
dotnet "%LUBAN_DLL%" ^
    -t client ^
    -c cs-bin ^
    --conf "%CONF_FILE%" ^
    --customTemplateDir "%CUSTOM_TEMPLATE_DIR%" ^
    -x "cs-bin.outputCodeDir=%PATH_GEN_CSHARP%" ^
    -x "pathValidator.rootDir=%ASSET_ROOT%" ^
    -x "lineEnding=lf"

set "exit_code=%ERRORLEVEL%"
if not "%exit_code%"=="0" (
    echo [ERROR] Luban C# export failed with exit code %exit_code%.
    goto :ON_ERROR
)

rem DataTables also contains UGF assets, so do not let Luban clean it.
dotnet "%LUBAN_DLL%" ^
    -t client ^
    -d json ^
    -d bin ^
    --conf "%CONF_FILE%" ^
    -x "json.outputDataDir=%PATH_DATA_JSON%" ^
    -x "bin.outputDataDir=%PATH_DATA_BIN%" ^
    -x "pathValidator.rootDir=%ASSET_ROOT%" ^
    -x "cleanUpOutputDir=false"

set "exit_code=%ERRORLEVEL%"
if not "%exit_code%"=="0" (
    echo [ERROR] Luban data export failed with exit code %exit_code%.
    goto :ON_ERROR
)

rem Convert exported json files to LF line endings to prevent git workspace pollution.
echo [INFO] Converting JSON files to LF line endings...
powershell -NoProfile -Command "Get-ChildItem -Path '%PATH_DATA_JSON%\*.json' | ForEach-Object { $c = [System.IO.File]::ReadAllText($_.FullName); if ($c.Contains([char]13)) { [System.IO.File]::WriteAllText($_.FullName, $c.Replace([string][char]13, ''), (New-Object System.Text.UTF8Encoding($false))) } }"

echo [INFO] Luban export completed.
popd
echo.
pause
exit /b 0

:ON_ERROR
popd
echo.
pause
exit /b 1

