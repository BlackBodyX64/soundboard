@echo off
echo ========================================
echo   TikTok Soundboard - Build Script
echo ========================================
echo.
echo Installing dependencies...
pip install -r requirements.txt
echo.
echo Building .exe...
pyinstaller --onefile --windowed --name "TikTok_Soundboard" --icon=NONE --add-data "sounds;sounds" soundboard.py
echo.
echo ========================================
echo   Build complete!
echo   EXE: dist\TikTok_Soundboard.exe
echo ========================================
pause
