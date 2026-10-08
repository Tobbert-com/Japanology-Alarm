@echo off
setlocal EnableExtensions
cd /d "%~dp0"

:: The first run needs administrator approval only to permit local-network access.
net session >nul 2>&1
if not "%errorlevel%"=="0" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

:: Reserve the HTTP address for this Windows user and expose it only on Private networks.
netsh http delete urlacl url=http://+:8123/ >nul 2>&1
netsh http add urlacl url=http://+:8123/ user="%USERDOMAIN%\%USERNAME%" >nul
netsh advfirewall firewall delete rule name="Japanology Alarm LAN Control" >nul 2>&1
netsh advfirewall firewall add rule name="Japanology Alarm LAN Control" dir=in action=allow protocol=TCP localport=8123 profile=private >nul

set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    msg * "Microsoft .NET Framework 4.8 is required. Install it, then run this file again."
    start "" "https://dotnet.microsoft.com/download/dotnet-framework/net48"
    exit /b 1
)

if not exist "JapanologyAlarm.exe" (
    "%CSC%" /nologo /target:winexe /out:"JapanologyAlarm.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Web.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll "Program_v3.cs"
    if errorlevel 1 (
        pause
        exit /b 1
    )
)

start "" "%~dp0JapanologyAlarm.exe"
exit /b
