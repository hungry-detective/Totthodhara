@echo off
setlocal
cd /d "%~dp0"

rem === Totthodhara portable packager ===
rem Result (shareable, runs anywhere, no Qt needed):
rem   deploy\Totthodhara.exe   (launcher stub, double-click this)
rem   deploy\data\             (history.db + settings, auto-created, NEVER deleted)
rem   deploy\library\          (real exe + all Qt DLLs/plugins/QML)
rem The stub sets TOTTHODHARA_DATA_DIR=<root>\data and starts library\.
rem Re-run this after ANY code change (it wipes deploy except data\).

set "QT_BIN=C:\Qt\6.8.3\mingw_64\bin"
set "QT_TOOLS=C:\Qt\Tools\mingw1310_64\bin"
set "CMAKE_BIN=C:\Program Files\CMake\bin"
set "NINJA_BIN=%LOCALAPPDATA%\Microsoft\WinGet\Packages\Ninja-build.Ninja_Microsoft.Winget.Source_8wekyb3d8bbwe"
set "PATH=%CMAKE_BIN%;%NINJA_BIN%;%QT_TOOLS%;%QT_BIN%;%PATH%"

where cmake >nul 2>nul
if %errorlevel% neq 0 (echo [package] cmake not found & exit /b 1)
where ninja >nul 2>nul
if %errorlevel% neq 0 (echo [package] ninja not found & exit /b 1)

rem --- Kill anything running (locked exe + single-instance mutex) ---
rem Kill-gate with lock verification (like dev.cmd): wiping deploy/ while
rem an exe is locked ships a mixed-version folder with zero errors.
taskkill /F /IM Totthodhara.exe >nul 2>&1
if not exist "build-release\Totthodhara.exe" goto lockfree
set TRIES=0
:waitlock
del /F /Q "%TEMP%\__tott_pkgtest.exe" >nul 2>&1
copy /B /Y "build-release\Totthodhara.exe" "%TEMP%\__tott_pkgtest.exe" >nul 2>&1
if %errorlevel%==0 goto lockfree
set /a TRIES+=1
if %TRIES% GEQ 20 (echo [package] exe still locked, kill Totthodhara.exe manually & exit /b 1)
ping -n 2 127.0.0.1 >nul
taskkill /F /IM Totthodhara.exe >nul 2>&1
goto waitlock
:lockfree
del /F /Q "%TEMP%\__tott_pkgtest.exe" >nul 2>&1
rem A running PORTABLE copy locks deploy\library\ too (same exe name):
rem probe it the same way, or the wipe below leaves stale DLLs behind.
if not exist "deploy\library\Totthodhara.exe" goto nodeploylock
del /F /Q "%TEMP%\__tott_pkgtest.exe" >nul 2>&1
copy /B /Y "deploy\library\Totthodhara.exe" "%TEMP%\__tott_pkgtest.exe" >nul 2>&1
if %errorlevel%==0 goto nodeploylock
echo [package] deploy copy still locked, kill Totthodhara.exe manually & exit /b 1
:nodeploylock
del /F /Q "%TEMP%\__tott_pkgtest.exe" >nul 2>&1

rem --- Configure Release fresh (picks up CMakeLists changes) ---
cmake -S . -B build-release -G Ninja -DCMAKE_BUILD_TYPE=Release "-DCMAKE_PREFIX_PATH=C:/Qt/6.8.3/mingw_64" "-DCMAKE_CXX_COMPILER=C:/Qt/Tools/mingw1310_64/bin/g++.exe"
if %errorlevel% neq 0 exit /b %errorlevel%
cmake --build build-release
if %errorlevel% neq 0 exit /b %errorlevel%

rem --- Clean deploy, KEEPING data\ (user history + settings) ---
if not exist "deploy\data" mkdir "deploy\data"
for /D %%D in ("deploy\*") do if /I not "%%~nxD"=="data" rmdir /S /Q "%%D"
del /Q "deploy\*" >nul 2>&1
rem A locked survivor above leaves library\ behind: refuse to ship a mix.
if exist "deploy\library" (echo [package] deploy wipe incomplete, kill Totthodhara.exe manually & exit /b 1)
mkdir "deploy\library"

rem --- Real exe + QML module live in library\ (auto-found next to exe) ---
copy /B /Y "build-release\Totthodhara.exe" "deploy\library\Totthodhara.exe"
if %errorlevel% neq 0 exit /b %errorlevel%
xcopy "build-release\Totthodhara" "deploy\library\Totthodhara\" /E /I /Y >nul
if %errorlevel% neq 0 exit /b %errorlevel%

rem --- Qt framework into library\ (scans --qmldir for QML imports incl. QtCore Settings) ---
windeployqt --dir deploy\library --qmldir . --release --compiler-runtime deploy\library\Totthodhara.exe
if %errorlevel% neq 0 exit /b %errorlevel%

rem --- Launcher stub becomes the root exe friends double-click ---
copy /B /Y "build-release\TotthodharaLauncher.exe" "deploy\Totthodhara.exe"
if %errorlevel% neq 0 exit /b %errorlevel%

rem --- Self-updater window rides in library/ (copied to temp at update time) ---
copy /B /Y "build-release\TotthodharaUpdater.exe" "deploy\library\TotthodharaUpdater.exe"
if %errorlevel% neq 0 exit /b %errorlevel%
windeployqt --dir deploy\library --release --compiler-runtime deploy\library\TotthodharaUpdater.exe
if %errorlevel% neq 0 exit /b %errorlevel%

rem --- Release zip for GitHub Releases (exe + library, NEVER data) ---
powershell -NoProfile -Command "Compress-Archive -Force -Path 'deploy\Totthodhara.exe','deploy\library' -DestinationPath 'Totthodhara-windows-portable.zip'"
if %errorlevel% neq 0 exit /b %errorlevel%

rem --- Integrity hash for the in-app updater (upload beside the zip) ---
powershell -NoProfile -Command "$h=(Get-FileHash 'Totthodhara-windows-portable.zip' -Algorithm SHA256).Hash.ToLower(); $h + '  Totthodhara-windows-portable.zip' | Out-File 'Totthodhara-windows-portable.zip.sha256' -Encoding ascii"
if %errorlevel% neq 0 exit /b %errorlevel%

echo [package] OK: deploy\ = Totthodhara.exe + data\ + library\
