@echo off
setlocal

cd /d "%~dp0"

set "MYSQL_EXE="
if exist "%ProgramFiles%\MySQL\MySQL Server 8.0\bin\mysql.exe" (
    set "MYSQL_EXE=%ProgramFiles%\MySQL\MySQL Server 8.0\bin\mysql.exe"
) else if exist "%ProgramFiles(x86)%\MySQL\MySQL Server 8.0\bin\mysql.exe" (
    set "MYSQL_EXE=%ProgramFiles(x86)%\MySQL\MySQL Server 8.0\bin\mysql.exe"
) else (
    for /f "delims=" %%I in ('where mysql 2^>nul') do (
        if not defined MYSQL_EXE set "MYSQL_EXE=%%I"
    )
)

if not defined MYSQL_EXE (
    echo [ERROR] mysql.exe was not found.
    echo Install MySQL Server 8.0 or add mysql.exe to PATH.
    pause
    exit /b 1
)

set /p MYSQL_ROOT_PASSWORD=Enter MySQL root password:

echo [INFO] Initializing mpsmesdb...
"%MYSQL_EXE%" -h localhost -P 3306 -u root -p%MYSQL_ROOT_PASSWORD% < database\init_db.sql
if errorlevel 1 (
    echo.
    echo [ERROR] Database setup failed.
    pause
    exit /b 1
)

echo.
echo [OK] Database is ready.
pause
exit /b 0
