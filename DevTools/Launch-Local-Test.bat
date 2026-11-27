@echo off
setlocal
set "LOADER=%~dp0pyRevit-local\bin\netfx\engines\IPY2712PR\pyRevitLoader.dll"
set "KSTRUCT=%APPDATA%\Autodesk\Revit\Addins\2024\KStruct.addin"
set "REVIT=C:\Program Files\Autodesk\Revit 2024\Revit.exe"
if not exist "%LOADER%" (echo Missing DevTools\pyRevit-local. & exit /b 1)
if not exist "%KSTRUCT%" (echo Missing current-user KStruct Revit 2024 manifest. & exit /b 2)
if not exist "%REVIT%" (echo Revit 2024 was not found at its default location. & exit /b 3)
set "TRIAL=%TEMP%\KStructDev-%RANDOM%-%RANDOM%"
if exist "%TRIAL%" (echo Temporary launch directory already exists. Retry. & exit /b 4)
mkdir "%TRIAL%" || exit /b 5
copy /y "%KSTRUCT%" "%TRIAL%\KStruct.addin" >nul || exit /b 6
> "%TRIAL%\pyRevit.addin" echo ^<?xml version="1.0" encoding="utf-8"?^>
>>"%TRIAL%\pyRevit.addin" echo ^<RevitAddIns^>
>>"%TRIAL%\pyRevit.addin" echo ^<AddIn Type="Application"^>
>>"%TRIAL%\pyRevit.addin" echo ^<Name^>PyRevitLoader^</Name^>
>>"%TRIAL%\pyRevit.addin" echo ^<Assembly^>%LOADER%^</Assembly^>
>>"%TRIAL%\pyRevit.addin" echo ^<AddInId^>B39107C3-A1D7-47F4-A5A1-532DDF6EDB5D^</AddInId^>
>>"%TRIAL%\pyRevit.addin" echo ^<FullClassName^>PyRevitLoader.PyRevitLoaderApplication^</FullClassName^>
>>"%TRIAL%\pyRevit.addin" echo ^<VendorId^>eirannejad^</VendorId^>
>>"%TRIAL%\pyRevit.addin" echo ^</AddIn^>
>>"%TRIAL%\pyRevit.addin" echo ^</RevitAddIns^>
> "%TRIAL%\Launch.txt" echo ' KStruct DevTools local test launch
>>"%TRIAL%\Launch.txt" echo Dim Jrn
>>"%TRIAL%\Launch.txt" echo Set Jrn = CrsJournalScript
if /i "%~1"=="--prepare-only" (echo Prepared: %TRIAL% & exit /b 0)
start "" "%REVIT%" "%TRIAL%\Launch.txt"
if errorlevel 1 exit /b 7
echo Revit launched for testing. Temporary files: %TRIAL%
echo Other third-party add-ins may not be loaded in journal mode.
exit /b 0
