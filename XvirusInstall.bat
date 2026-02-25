@echo off

net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

sc create XvirusService binPath= "%~dp0XvirusService.exe" start= auto DisplayName= "Xvirus Service"
sc start XvirusService

mshta "javascript:var sh=new ActiveXObject('WScript.Shell');sh.Popup('Installed successfully',0,'Xvirus',64);close();"
