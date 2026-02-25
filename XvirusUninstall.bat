@echo off

net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

sc stop XvirusService
timeout /t 2 /nobreak >nul
sc delete XvirusService

mshta "javascript:var sh=new ActiveXObject('WScript.Shell');sh.Popup('Successfully uninstalled. You can now delete the folder.',0,'Xvirus',64);close();"
