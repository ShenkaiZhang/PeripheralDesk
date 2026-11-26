@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
"%CSC%" /nologo /optimize+ /codepage:65001 /langversion:5 /platform:x64 /target:exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /win32icon:PeripheralDesk.ico /resource:PeripheralDesk.ico,PeripheralDesk.AppIcon /out:PeripheralDesk.Tests.exe Program.cs Design.cs Audio.cs Devices.cs Logitech.cs HyperX.cs Fa09.cs BluetoothStatus.cs Protocol.cs RazerMouse.cs CompxMouse.cs SelfTest.cs
if errorlevel 1 exit /b 1
PeripheralDesk.Tests.exe --self-test
if errorlevel 1 exit /b 1
PeripheralDesk.Tests.exe --verify-ui
exit /b %errorlevel%
