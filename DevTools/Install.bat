@echo off
setlocal
set "SRC=%~dp0KStructDev.extension"
set "DST=%APPDATA%\pyRevit\Extensions\KStructDev.extension"
if not exist "%SRC%\" exit /b 1
if exist "%DST%\" (
 echo Existing extension found. Remove or back it up before installing.
 exit /b 2
)
robocopy "%SRC%" "%DST%" /E
if errorlevel 8 exit /b 3
echo Installed. Ensure the parent folder is configured in pyRevit extension paths, then reload pyRevit.
exit /b 0
