@echo off
setlocal
cd /d "%~dp0"
title Compilando SysDiag

rem No ejecutar un instalador remoto desde un temporal predecible.
rem Requiere SDK 8 segun global.json; el programa publicado es autocontenido.
set "DOTNET_EXE=dotnet"
if exist "%LOCALAPPDATA%\SysDiag\dotnet-sdk\dotnet.exe" (
    set "DOTNET_EXE=%LOCALAPPDATA%\SysDiag\dotnet-sdk\dotnet.exe"
    set "DOTNET_ROOT=%LOCALAPPDATA%\SysDiag\dotnet-sdk"
)
"%DOTNET_EXE%" --version
if errorlevel 1 goto sdk_error
"%DOTNET_EXE%" restore SysDiag.csproj
if errorlevel 1 goto build_error
"%DOTNET_EXE%" publish SysDiag.csproj -c Release -o publish
if errorlevel 1 goto build_error
rem -ExecutionPolicy Bypass solo rige para esta sesion de PowerShell: no cambia la
rem politica del sistema. Necesario cuando Windows trae la politica restrictiva
rem (Restricted/AllSigned) o los archivos traen marca de web por el ZIP de GitHub.
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\validate_release.ps1" -Root "%~dp0publish"
if errorlevel 1 goto validate_error
echo Listo: %CD%\publish\SysDiag.exe
pause
exit /b 0

:sdk_error
echo Instala el SDK .NET 8 desde https://dotnet.microsoft.com/download/dotnet/8.0
echo No se descargan ni ejecutan scripts externos automaticamente.
pause
exit /b 1

:validate_error
echo.
echo La compilacion y la publicacion terminaron, pero la validacion de release fallo.
echo El ejecutable quedo en %CD%\publish; revisa los mensajes anteriores.
echo Si el error menciona ExecutionPolicy, UnauthorizedAccess o "ejecucion de scripts
echo deshabilitada", una directiva de seguridad de Windows bloquea los scripts de
echo PowerShell. build.bat ya usa -ExecutionPolicy Bypass, pero una directiva de grupo
echo puede imponer una politica mas estricta que exime a toda la maquina.
pause
exit /b 1

:build_error
echo Fallo la compilacion. Revisa los mensajes anteriores.
pause
exit /b 1
