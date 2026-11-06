@echo off
setlocal
cd /d "%~dp0"

rem Uses the C# compiler that ships with every Windows 10/11 install (.NET Framework 4.x).
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Could not find csc.exe under %WINDIR%\Microsoft.NET - is .NET Framework 4.x installed?
    pause
    exit /b 1
)

echo Building WowTiler.exe ...
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /out:WowTiler.exe /win32icon:WowTiler.ico ^
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
    WowTiler.cs
if errorlevel 1 (
    echo.
    echo Build FAILED - see errors above.
    pause
    exit /b 1
)

echo.
echo Done: %~dp0WowTiler.exe
pause
