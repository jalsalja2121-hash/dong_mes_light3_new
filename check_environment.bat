@echo off
setlocal

cd /d "%~dp0"

echo === MySQL service ===
sc query MySQL80
echo.

echo === MySQL port 3306 ===
powershell -NoProfile -ExecutionPolicy Bypass -Command "Test-NetConnection localhost -Port 3306"
echo.

echo === Python environment ===
set "APP_PY="
for /f "usebackq delims=" %%I in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$j=Get-Content 'MpsMes.WPF\appsettings.json' -Raw | ConvertFrom-Json; $j.Vision.PythonExePath"`) do set "APP_PY=%%I"
if exist "%APP_PY%" (
    "%APP_PY%" --version
    set "YOLO_CONFIG_DIR=%~dp0"
    "%APP_PY%" -c "import torch; from ultralytics import YOLO; from PIL import Image; import cv2, numpy; print('torch', torch.__version__); print('ultralytics/pillow/opencv/numpy OK')"
) else (
    echo [WARN] PythonExePath was not found: %APP_PY%
    echo Run setup_python_env.bat to create/update the conda mpsmes environment.
)
echo.

echo === Model file ===
if exist "Models\2class_best.pt" (
    echo [OK] Models\2class_best.pt
) else if exist "2class_best.pt" (
    echo [OK] 2class_best.pt
) else (
    echo [WARN] Model file was not found.
)
echo.

pause
