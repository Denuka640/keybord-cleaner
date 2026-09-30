@echo off
title KeyShield — Build and Publish
color 0A
echo.
echo  ===============================================
echo    KeyShield — Keyboard Cleaner
echo    Building standalone .exe for Windows x64
echo  ===============================================
echo.

dotnet publish -c Release -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:PublishReadyToRun=true ^
  -o publish

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo  [ERROR] Build failed. See output above.
    pause
    exit /b 1
)

echo.
echo  ===============================================
echo   SUCCESS! Standalone exe is in:
echo   publish\KeyShield.exe
echo  ===============================================
echo.

set /p OPEN="  Open the publish folder now? (Y/N): "
if /i "%OPEN%"=="Y" explorer publish

pause
