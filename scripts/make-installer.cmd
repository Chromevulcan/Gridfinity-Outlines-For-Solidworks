@echo off
rem Builds the solution in Release, then the installer. Needs the .NET SDK (or Visual Studio) and Inno Setup 6.3 or newer (7 works).
setlocal
cd /d "%~dp0.."
dotnet build GridfinityOutlines.sln -c Release || goto :fail

set ISCC=
for %%V in (7 6) do (
  if not defined ISCC if exist "%ProgramFiles(x86)%\Inno Setup %%V\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup %%V\ISCC.exe"
  if not defined ISCC if exist "%ProgramFiles%\Inno Setup %%V\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup %%V\ISCC.exe"
  if not defined ISCC if exist "%LocalAppData%\Programs\Inno Setup %%V\ISCC.exe" set "ISCC=%LocalAppData%\Programs\Inno Setup %%V\ISCC.exe"
)
if not defined ISCC (
  echo Inno Setup was not found in the usual folders.
  echo Open installer\GridfinityOutlines.iss in the Inno Setup compiler and press Compile, or edit this file to point at ISCC.exe.
  goto :fail
)
echo Using %ISCC%
"%ISCC%" installer\GridfinityOutlines.iss || goto :fail
echo.
echo Done: installer\Output\GridfinityOutlines-Setup.exe
exit /b 0
:fail
echo Failed.
exit /b 1
