@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET SDK 8 not found. Install: winget install Microsoft.DotNet.SDK.8
  pause & exit /b 1
)
if exist publish rmdir /s /q publish
dotnet publish Workspaces\Workspaces.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
if errorlevel 1 ( echo BUILD FAILED & pause & exit /b 1 )
echo.
echo Release build: %~dp0publish\Workspaces.exe

set ISCC=
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
if "%ISCC%"=="" (
  echo Inno Setup 6 not found: installer skipped. Install it: winget install JRSoftware.InnoSetup
  pause & exit /b 0
)
"%ISCC%" installer\Workspaces.iss
if errorlevel 1 ( echo INSTALLER FAILED & pause & exit /b 1 )
echo Installer: %~dp0dist-installer
pause
