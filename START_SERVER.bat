@echo off
REM CCL-MES — Standalone Server Launcher (Windows)  ***DA DONG BANG 2026-08-19***
REM
REM FROZEN. App Blazor Server legacy (:5050) khong con phuc vu nha may. Launcher
REM nay chi chay khi co MES_LEGACY_WEB_FORCE=1 (khoi phuc khan cap), xem khoi
REM "0. DONG BANG / FROZEN" ben duoi va
REM CCL-MES-Hybrid/docs/CUTOVER-LEGACY-WEB-FREEZE-2026-08-19.md
REM
REM Phase 6 Buoc 6.5 — mirror Ops Control v1.2's server-launcher pattern.
REM Double-click this file from Explorer to start the SQLite-backed server
REM on port 5050 (LAN-reachable). Ctrl+C in console to stop.
REM
REM Data folder:
REM   Default          : <repo-root>\data\ccl_mes.db
REM   Backup snapshots : <repo-root>\data\Backup\SQLite\
REM   Override base    : set MES_DATA_DIR=C:\absolute\path
REM   Override file    : set MES_DB_PATH=C:\absolute\file.db (rarely needed)

setlocal ENABLEDELAYEDEXPANSION

cd /d "%~dp0"

cls

REM -- 0. DONG BANG / FROZEN
REM GATE-ANCHOR: legacy-web-force-guard
REM Canh boi CCL-MES-Hybrid/scripts/gate-legacy-web-frozen.sh — khoi nay PHAI
REM nam TRUOC lenh `dotnet run`. Go no = gate do, khong phai "don dep".
if not "%MES_LEGACY_WEB_FORCE%"=="1" (
  echo.
  echo   ================================================================
  echo      [X]  UNG DUNG NAY DA NGUNG PHUC VU  -  2026-08-19
  echo           THIS APPLICATION IS RETIRED    -  2026-08-19
  echo   ================================================================
  echo.
  echo   VI  CCL-MES Blazor Server ^(:5050^) da dong bang. Khong con ai o nha
  echo       may dung ban nay; moi cong viec san xuat da chuyen sang app Hybrid.
  echo.
  echo       -^> Dung thay the :  CCL-MES Hybrid - API :5100 + app desktop MAUI
  echo       -^> Khoi dong API :  cd CCL-MES-Hybrid\src\CCL.MES.Api ^&^& dotnet run
  echo       -^> Tai lieu      :  CCL-MES-Hybrid/docs/CUTOVER-LEGACY-WEB-FREEZE-2026-08-19.md
  echo.
  echo   EN  CCL-MES Blazor Server ^(:5050^) is frozen. Nobody on the shop floor
  echo       uses it any more; all production work moved to the Hybrid app.
  echo.
  echo       -^> Use instead   :  CCL-MES Hybrid - API :5100 + MAUI desktop app
  echo       -^> Start the API :  cd CCL-MES-Hybrid\src\CCL.MES.Api ^&^& dotnet run
  echo       -^> Cutover doc   :  CCL-MES-Hybrid/docs/CUTOVER-LEGACY-WEB-FREEZE-2026-08-19.md
  echo.
  echo   ----------------------------------------------------------------
  echo   VI  Chay nham? Khong hong gi ca - chua khoi dong server, chua mo cong 5050.
  echo   EN  Ran this by mistake? Nothing broke - no server started, port 5050 untouched.
  echo.
  echo   VI  That su can chay lai de khoi phuc khan cap? Phai noi ro y dinh:
  echo   EN  Genuinely need it back for an emergency rollback? Say so explicitly:
  echo.
  echo         set MES_LEGACY_WEB_FORCE=1 ^&^& START_SERVER.bat
  echo.
  pause
  exit /b 2
)

echo.
echo   [!] MES_LEGACY_WEB_FORCE=1 - chay app DA DONG BANG theo yeu cau tuong minh.
echo   [!] MES_LEGACY_WEB_FORCE=1 - starting the FROZEN app on explicit request.
echo       VI  Chi dung de khoi phuc khan cap. Bao Henry sau khi xong.
echo       EN  Emergency rollback only. Tell Henry once you are done.

echo.
echo   ============================================================
echo        CCL-MES - Standalone Server (.NET 10)
echo        SQLite mode (Ops Control v1.2 pattern)
echo   ============================================================
echo.

REM -- 1. Tool check: dotnet
where dotnet >NUL 2>&1
if errorlevel 1 (
  echo   [X] .NET 10 chua duoc cai dat.
  echo       Tai SDK tu https://dotnet.microsoft.com/download
  pause
  exit /b 1
)

for /f "delims=" %%v in ('dotnet --version 2^>NUL') do set DOTNET_VERSION=%%v
echo   [OK] dotnet: !DOTNET_VERSION!
echo.

REM -- 2. Data folder preflight
if defined MES_DATA_DIR (
  set DATA_DIR=!MES_DATA_DIR!
) else (
  set DATA_DIR=%CD%\data
)
if not exist "!DATA_DIR!\Backup\SQLite" mkdir "!DATA_DIR!\Backup\SQLite" 2>NUL
if exist "!DATA_DIR!\ccl_mes.db" (
  for %%I in ("!DATA_DIR!\ccl_mes.db") do set DB_SIZE=%%~zI
  echo   [OK] DB:     !DATA_DIR!\ccl_mes.db ^(!DB_SIZE! bytes^)
) else (
  echo   [OK] DB se tao moi tai: !DATA_DIR!\ccl_mes.db ^(first boot^)
)
echo   [OK] Backup: !DATA_DIR!\Backup\SQLite\
echo.

REM -- 3. Detect LAN IP (first non-loopback IPv4)
set LOCAL_IP=
for /f "tokens=2 delims=:" %%i in ('ipconfig ^| findstr /R "IPv4"') do (
  if not defined LOCAL_IP (
    set LOCAL_IP=%%i
    set LOCAL_IP=!LOCAL_IP:~1!
  )
)
if not defined LOCAL_IP set LOCAL_IP=^<not detected^>

REM -- 4. Free port 5050 (best-effort)
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":5050.*LISTENING"') do (
  echo   [!] Port 5050 dang duoc giu boi PID %%p, dang dung...
  taskkill /F /PID %%p >NUL 2>&1
)

REM -- 5. Banner
echo.
echo   +------------------------------------------------------+
echo   ^|                                                      ^|
echo   ^|         CCL-MES SERVER starting...                   ^|
echo   ^|                                                      ^|
echo   ^|   Local:    http://localhost:5050                    ^|
echo   ^|   LAN:      http://!LOCAL_IP!:5050
echo   ^|                                                      ^|
echo   ^|   Data:     !DATA_DIR!
echo   ^|   Log:      console only (no tee on Windows)         ^|
echo   ^|                                                      ^|
echo   ^|   Stop:     Ctrl+C in this console                   ^|
echo   ^|                                                      ^|
echo   +------------------------------------------------------+
echo.
echo   ^>^>  Server output (Ctrl+C to stop):
echo.

REM -- 6. Launch
REM MES_LEGACY_WEB_DRYRUN=1 — kiem chung cong force ma KHONG bind 0.0.0.0:5050
REM len LAN nha may va KHONG cham data\ccl_mes.db. Dung boi gate + nguoi verify.
if "%MES_LEGACY_WEB_DRYRUN%"=="1" (
  echo   [DRYRUN] cong force da mo - se chay lenh sau ^(nhung khong chay^):
  echo   [DRYRUN] force gate opened - would run ^(but does not^):
  echo            set ASPNETCORE_URLS=http://0.0.0.0:5050 ^&^& dotnet run --project src\CCL.MES.Web --no-launch-profile
  exit /b 0
)

set ASPNETCORE_URLS=http://0.0.0.0:5050
dotnet run --project src\CCL.MES.Web --no-launch-profile

REM -- 7. Cleanup
echo.
echo   [OK] Server da dung.
pause
