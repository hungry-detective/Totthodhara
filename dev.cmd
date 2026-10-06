@echo off
setlocal
cd /d "%~dp0"

rem --- Dev env: Qt DLLs resolve via PATH (dev exe is NOT portable) ---
set "QT_BIN=C:\Qt\6.8.3\mingw_64\bin"
set "QT_TOOLS=C:\Qt\Tools\mingw1310_64\bin"
set "CMAKE_BIN=C:\Program Files\CMake\bin"
set "NINJA_BIN=%LOCALAPPDATA%\Microsoft\WinGet\Packages\Ninja-build.Ninja_Microsoft.Winget.Source_8wekyb3d8bbwe"
set "PATH=%CMAKE_BIN%;%NINJA_BIN%;%QT_TOOLS%;%QT_BIN%;%PATH%"

where cmake >nul 2>nul
if %errorlevel% neq 0 (echo [dev] cmake not found in "%CMAKE_BIN%" & exit /b 1)
where ninja >nul 2>nul
if %errorlevel% neq 0 (echo [dev] ninja not found in "%NINJA_BIN%" & exit /b 1)
if not exist "%QT_BIN%\Qt6Gui.dll" (echo [dev] Qt6Gui.dll missing at "%QT_BIN%" & exit /b 1)

rem --- Kill-gate: locked exe + mutex survivor fakes launches ---
taskkill /F /IM Totthodhara.exe >nul 2>&1
if not exist "build\Totthodhara.exe" goto dobuid
set TRIES=0
:waitlock
del /F /Q "%TEMP%\__tott_locktest.exe" >nul 2>&1
copy /B /Y "build\Totthodhara.exe" "%TEMP%\__tott_locktest.exe" >nul 2>&1
if %errorlevel%==0 goto lockfree
set /a TRIES+=1
if %TRIES% GEQ 20 (echo [dev] exe still locked, kill Totthodhara.exe manually & exit /b 1)
timeout /t 1 /nobreak >nul
taskkill /F /IM Totthodhara.exe >nul 2>&1
goto waitlock
:lockfree
del /F /Q "%TEMP%\__tott_locktest.exe" >nul 2>&1

:dobuid
rem Reconfigure every run (seconds under Ninja): CMakeLists/QML-list
rem changes otherwise build stale without warning.
cmake -S . -B build -G Ninja -DCMAKE_BUILD_TYPE=Debug "-DCMAKE_PREFIX_PATH=C:/Qt/6.8.3/mingw_64" "-DCMAKE_CXX_COMPILER=C:/Qt/Tools/mingw1310_64/bin/g++.exe"
if %errorlevel% neq 0 exit /b %errorlevel%
cmake --build build
if %errorlevel% neq 0 exit /b %errorlevel%

rem --- Launch with working dir = Totthodhara/ so data/ lands in the right place ---
start "" /D "%~dp0" "build\Totthodhara.exe" %*
echo [dev] launched. Tip: always run via dev.cmd (never double-click build exe) so Qt DLLs resolve.
