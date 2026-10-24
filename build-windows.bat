@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET 10 SDK not found. Install from https://dotnet.microsoft.com/download/dotnet/10.0
  pause
  exit /b 1
)
dotnet publish src\RioIsland\RioIsland.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -o build
if errorlevel 1 (
  echo BUILD FAILED. See messages above.
  pause
  exit /b 1
)
echo.
echo Build complete. Open build\TheIsland.exe
pause
