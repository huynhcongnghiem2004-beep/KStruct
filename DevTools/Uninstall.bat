@echo off
setlocal
set "DST=%APPDATA%\pyRevit\Extensions\KStructDev.extension"
if not exist "%DST%\" exit /b 0
choice /M "Remove KStructDev.extension only"
if errorlevel 2 exit /b 1
rmdir /s /q "%DST%"
if exist "%DST%\" exit /b 2
echo Reload pyRevit.
