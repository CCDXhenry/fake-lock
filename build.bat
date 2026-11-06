@echo off
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe

echo Compiling FakeLock.cs...
"%CSC%" /target:winexe /optimize+ /platform:anycpu /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll /out:FakeLock.exe FakeLock.cs

if %ERRORLEVEL% equ 0 (
    echo [OK] Build succeeded: FakeLock.exe
) else (
    echo [ERROR] Build failed with code %ERRORLEVEL%
)
