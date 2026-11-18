@echo off
setlocal
cd /d "%~dp0"

dotnet build HYZMouseHub.csproj -c Release
if errorlevel 1 (
  echo.
  echo 编译失败，请检查上面的错误信息。
  exit /b 1
)

copy /Y "bin\Release\net48\HYZMouseHub.exe" "HYZMouseHub.exe" >nul
echo.
echo 已生成: %~dp0HYZMouseHub.exe
echo 运行参数示例:
echo   HYZMouseHub.exe --list
echo   HYZMouseHub.exe --test
echo   HYZMouseHub.exe
endlocal
