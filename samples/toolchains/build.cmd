@echo off
rem Build and run the tool-chain samples (docs\vision-tools\VISIONPRO_TOOLS.md).
rem Needs: Visual Studio 2022 (Roslyn csc), Cognex VisionPro 9.x x64 with a license.
rem   build.cmd          build + make jobs + self-check against images\truth.csv
rem   build.cmd shots    also capture editor screenshots into docs\vision-tools\shots
setlocal enabledelayedexpansion
cd /d "%~dp0"
if "%VPRO_ROOT%"=="" set "VPRO_ROOT=C:\Program Files\Cognex\VisionPro"
if "%CSC%"=="" set "CSC=C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe"
set "REFS="
for %%f in ("%VPRO_ROOT%\ReferencedAssemblies\Cognex.VisionPro*.dll") do (
  echo %%~nxf | findstr /i "3D" >nul || set "REFS=!REFS! /r:"%%f""
)
"%CSC%" /nologo /target:exe /platform:x64 /out:MakeToolChainJobs.exe !REFS! MakeToolChainJobs.cs || exit /b 1
"%CSC%" /nologo /target:winexe /platform:x64 /out:ToolShots.exe !REFS! /r:System.Windows.Forms.dll /r:System.Drawing.dll ToolShots.cs || exit /b 1
"%~dp0MakeToolChainJobs.exe" jobs images
if errorlevel 1 (
  echo Self-check FAILED - see MISMATCH lines above.
  exit /b 1
)
if /i "%1"=="shots" "%~dp0ToolShots.exe" jobs images\ok_03.bmp ..\..\docs\vision-tools\shots
endlocal
