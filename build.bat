@echo off
echo ========================================
echo   TikTok Soundboard - Build Script
echo ========================================
echo.
echo Installing dependencies...
pip install -r requirements.txt
echo.
echo Building Main App...
python -m PyInstaller --onefile --windowed --name "TikTok_Soundboard" --icon=NONE --add-data "sounds;sounds" soundboard.py
echo.
echo Building Updater...
python -m PyInstaller --onefile --windowed --name "TikTok_Updater" --icon=NONE version.py
echo.
echo ========================================
echo   Build complete!
echo   Main EXE: dist\TikTok_Soundboard.exe
echo   Updater EXE: dist\TikTok_Updater.exe
echo ========================================
pause
