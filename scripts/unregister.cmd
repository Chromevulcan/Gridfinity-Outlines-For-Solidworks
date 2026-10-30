@echo off
rem Removes the add-in registration. Run from an ADMINISTRATOR command prompt.
rem Usage: unregister.cmd [path\to\GridfinityOutlines.Sw.dll]
setlocal
set DLL=%~1
if "%DLL%"=="" set DLL=%~dp0..\src\Gridfinity.Sw\bin\Release\net48\GridfinityOutlines.Sw.dll

"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\regasm.exe" /unregister "%DLL%"
endlocal
