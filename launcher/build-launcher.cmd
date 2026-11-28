@echo off
rem Builds "Julia's Launcher.exe" (the edupode launcher) in the repository's root with the C# compiler that ships with Windows.
rem   build-launcher.cmd            -> "Julia's Launcher.exe" beside the repository
rem   build-launcher.cmd <out.exe>  -> that file instead (the release workflow, tests)
rem /deterministic is not known to this compiler: the same sources give the same program apart
rem from its time stamp and GUID, which scripts\compare-launcher.ps1 leaves out when it compares.
setlocal EnableDelayedExpansion
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo The .NET Framework 4 C# compiler was not found.
  exit /b 1
)
set "OUT=%~1"
if "%OUT%"=="" set "OUT=%~dp0..\Julia's Launcher.exe"
rem Each translation in lang\ goes into the program as the resource lang.<code>.json (Languages.cs).
set "RES="
for %%f in ("%~dp0lang\*.json") do set RES=!RES! "/resource:%%~ff,lang.%%~nf.json"
"%CSC%" /nologo /target:winexe /optimize+ /win32icon:"%~dp0icon.ico" /out:"%OUT%" ^
  /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:Microsoft.VisualBasic.dll ^
  /reference:System.Core.dll /reference:System.Management.dll /reference:System.Web.Extensions.dll ^
  /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll !RES! ^
  "%~dp0VIctoryLauncher.cs" "%~dp0Widgets.cs" "%~dp0SmoothMotion.cs" "%~dp0Gpus.cs" "%~dp0Saves.cs" "%~dp0Setup.cs" "%~dp0Session.cs" ^
  "%~dp0Hardware.cs" "%~dp0Updater.cs" "%~dp0ReleaseKey.cs" "%~dp0Reports.cs" "%~dp0Custom.cs" "%~dp0ShadersPage.cs" "%~dp0SessionLogs.cs" "%~dp0WhatsNew.cs" "%~dp0HardwareStats.cs" "%~dp0Cleanup.cs" "%~dp0Storage.cs" "%~dp0Agility.cs" "%~dp0French.cs" "%~dp0Languages.cs"
if errorlevel 1 exit /b 1
echo Built: "%OUT%"
