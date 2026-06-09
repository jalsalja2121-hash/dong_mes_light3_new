# MPS MES deployment checklist

1. Install MySQL Server 8.0.
2. Run `setup_db.bat` and enter the MySQL root password.
3. Install Anaconda or Miniconda.
4. Run `setup_python_env.bat` to create/update the `mpsmes` conda environment.
5. Put the model at `Models\2class_best.pt` or edit `MpsMes.WPF\appsettings.json`.
6. Install MX Component 5.0 on the PLC PC.
7. Run `check_environment.bat`.
8. Run the WPF app.

The app uses `appsettings.json` for DB, PLC, Python, model, and image paths.
