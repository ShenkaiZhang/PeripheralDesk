@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo .NET Framework x64 C# compiler not found.
  exit /b 1
)
set "FILES=Program.cs Design.cs Audio.cs AudioSessions.cs AudioMonitor.cs Mixer.cs Devices.cs Logitech.cs HyperX.cs Fa09.cs BluetoothStatus.cs Protocol.cs RazerMouse.cs CompxMouse.cs SelfTest.cs"
"%CSC%" /nologo /optimize+ /codepage:65001 /langversion:5 /platform:x64 /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /win32icon:PeripheralDesk.ico /resource:PeripheralDesk.ico,PeripheralDesk.AppIcon /out:PeripheralDesk.exe %FILES%
exit /b %errorlevel%
