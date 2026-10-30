@echo off
rem Registers the add-in with SolidWorks. Run from an ADMINISTRATOR command prompt.
rem Usage: register.cmd [path\to\GridfinityOutlines.Sw.dll]
setlocal
set DLL=%~1
if "%DLL%"=="" set DLL=%~dp0..\src\Gridfinity.Sw\bin\Release\net48\GridfinityOutlines.Sw.dll

if not exist "%DLL%" (
  echo Could not find "%DLL%". Build the solution in Release first, or pass the DLL path.
  exit /b 1
)

"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\regasm.exe" /codebase "%DLL%"
if errorlevel 1 (
  echo Registration failed. Are you running as Administrator?
  exit /b 1
)
echo Registered. Restart SolidWorks; the add-in appears under Tools ^> Add-Ins and in the Gridfinity menu.
endlocal
