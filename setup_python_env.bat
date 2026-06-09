@echo off
setlocal EnableExtensions

cd /d "%~dp0"

set "ENV_NAME=mpsmes"
set "CONDA_BAT="

if exist "%USERPROFILE%\anaconda3\condabin\conda.bat" set "CONDA_BAT=%USERPROFILE%\anaconda3\condabin\conda.bat"
if not defined CONDA_BAT if exist "%USERPROFILE%\miniconda3\condabin\conda.bat" set "CONDA_BAT=%USERPROFILE%\miniconda3\condabin\conda.bat"
if not defined CONDA_BAT if exist "%ProgramData%\anaconda3\condabin\conda.bat" set "CONDA_BAT=%ProgramData%\anaconda3\condabin\conda.bat"
if not defined CONDA_BAT if exist "%ProgramData%\miniconda3\condabin\conda.bat" set "CONDA_BAT=%ProgramData%\miniconda3\condabin\conda.bat"

if not defined CONDA_BAT (
    for /f "delims=" %%I in ('where conda.bat 2^>nul') do (
        if not defined CONDA_BAT set "CONDA_BAT=%%I"
    )
)

if not defined CONDA_BAT (
    echo [ERROR] Conda was not found.
    echo Install Anaconda or Miniconda, then run this script again.
    pause
    exit /b 1
)

echo [INFO] Conda: "%CONDA_BAT%"

call "%CONDA_BAT%" info --envs | findstr /R /C:"^%ENV_NAME% " >nul
if errorlevel 1 (
    echo [INFO] Creating conda environment '%ENV_NAME%' with Python 3.10...
    call "%CONDA_BAT%" create -n %ENV_NAME% python=3.10 -y
    if errorlevel 1 goto fail
) else (
    echo [INFO] Conda environment '%ENV_NAME%' already exists.
)

call "%CONDA_BAT%" activate %ENV_NAME%
if errorlevel 1 goto fail

for /f "delims=" %%I in ('python -c "import sys; print(sys.executable)"') do set "PYTHON_EXE=%%I"

echo [INFO] Python: "%PYTHON_EXE%"
python --version
if errorlevel 1 goto fail

echo [INFO] Upgrading pip...
python -m pip install --upgrade pip
if errorlevel 1 goto fail

echo [INFO] Installing CPU PyTorch...
python -m pip install torch torchvision --index-url https://download.pytorch.org/whl/cpu
if errorlevel 1 goto fail

echo [INFO] Installing remaining packages...
python -m pip install -r requirements.txt
if errorlevel 1 goto fail

echo [INFO] Verifying packages...
set "YOLO_CONFIG_DIR=%~dp0"
python -c "import torch; from ultralytics import YOLO; from PIL import Image; import cv2, numpy; print('torch', torch.__version__); print('ultralytics/pillow/opencv/numpy OK')"
if errorlevel 1 goto fail

echo [INFO] Updating appsettings.json PythonExePath...
set "MPSMES_PYTHON_EXE=%PYTHON_EXE%"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$path = 'MpsMes.WPF\appsettings.json';" ^
  "$json = Get-Content $path -Raw | ConvertFrom-Json;" ^
  "$json.Vision.PythonExePath = $env:MPSMES_PYTHON_EXE;" ^
  "$json | ConvertTo-Json -Depth 10 | Set-Content $path -Encoding UTF8"
if errorlevel 1 goto fail

echo.
echo [OK] Conda environment is ready.
echo PythonExePath: "%PYTHON_EXE%"
echo Run check_environment.bat to verify the full setup.
pause
exit /b 0

:fail
echo.
echo [ERROR] Python/Conda environment setup failed.
pause
exit /b 1
