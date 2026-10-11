# Instala el setup de Inno Setup en silencio, comprueba que el EXE instalado arranca
# con --self-test y lo desinstala. Cubre lo que antes se comprobaba a mano (MEJORAS, seccion 2, item 11).
#
# Solo ASCII: Windows PowerShell 5.1 lee un .ps1 sin BOM como ANSI.
param(
    [string]$Setup = '',
    [int]$TimeoutSeconds = 300
)
$ErrorActionPreference = 'Stop'

# La ubicación del setup no se adivina: installer\SysDiag.iss declara
# OutputDir={#SourcePath}\Output, y Tools\validate_docs.ps1 comprueba que esta
# puerta siga mirando ahí. El archivo junto al .iss se acepta como segunda
# opción porque un ISCC invocado a mano con otro OutputDir deja el setup ahí.
# (Antes se buscaba solo junto al .iss, mientras Inno escribía en Output\: la
# puerta fallaba después de compilar, que es la peor forma de fallar.)
if ([string]::IsNullOrWhiteSpace($Setup)) {
    $candidatos = @(
        (Join-Path $PSScriptRoot '..\installer\Output\SysDiag-Setup-*.exe'),
        (Join-Path $PSScriptRoot '..\installer\SysDiag-Setup-*.exe')
    )
} else {
    $candidatos = @($Setup)
}
$instalador = @($candidatos | ForEach-Object { Get-ChildItem -Path $_ -ErrorAction SilentlyContinue })
if ($instalador.Count -ne 1) {
    throw "Se esperaba un instalador y hay $($instalador.Count). Rutas buscadas: $($candidatos -join '; ')."
}
$instalador = $instalador[0].FullName

$destino = Join-Path $env:ProgramFiles 'SysDiag'
$exe = Join-Path $destino 'SysDiag.exe'
$desinstalador = Join-Path $destino 'unins000.exe'
$log = Join-Path $env:TEMP 'sysdiag-install.log'

function Esperar([System.Diagnostics.Process]$proceso, [string]$paso) {
    if (-not $proceso.WaitForExit($TimeoutSeconds * 1000)) {
        $proceso.Kill()
        throw "$paso no termino en $TimeoutSeconds s."
    }
    return $proceso.ExitCode
}

Write-Output "Instalando en silencio: $(Split-Path $instalador -Leaf)"
$p = Start-Process -FilePath $instalador -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/LOG=$log" -PassThru
$codigo = Esperar $p 'La instalacion'
if ($codigo -ne 0) {
    if (Test-Path $log) { Get-Content $log -Tail 40 | Write-Output }
    throw "La instalacion termino con codigo $codigo."
}
if (-not (Test-Path $exe)) { throw "No se instalo $exe." }

Write-Output 'Autotest del EXE instalado (--self-test)'
# Mismo criterio que validate_release.ps1: codigo 0 Y la marca en stdout. Un ejecutable
# que sale con 0 sin haber cargado WPF no pasa.
$info = New-Object System.Diagnostics.ProcessStartInfo
$info.FileName = $exe
$info.Arguments = '--self-test'
$info.UseShellExecute = $false
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
$p = [System.Diagnostics.Process]::Start($info)
$salida = $p.StandardOutput.ReadToEndAsync()
$errores = $p.StandardError.ReadToEndAsync()
$codigo = Esperar $p 'El autotest'
$texto = $salida.Result
if ($codigo -ne 0 -or $texto -notmatch 'SYSDIAG_SELF_TEST_OK') {
    Write-Output $texto
    Write-Output $errores.Result
    throw "El autotest del EXE instalado fallo (codigo $codigo)."
}

Write-Output 'Desinstalando en silencio'
if (-not (Test-Path $desinstalador)) { throw "Falta el desinstalador: $desinstalador" }
$p = Start-Process -FilePath $desinstalador -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -PassThru
$codigo = Esperar $p 'La desinstalacion'
if ($codigo -ne 0) { throw "La desinstalacion termino con codigo $codigo." }
if (Test-Path $exe) { throw "La desinstalacion dejo $exe." }

Write-Output 'Instalador verificado: instala, arranca con --self-test y desinstala.'
