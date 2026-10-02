param([string]$Root = (Join-Path $PSScriptRoot '..\publish'))
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path $Root).Path
$exe = Join-Path $Root 'SysDiag.exe'
if (-not (Test-Path $exe) -or (Get-Item $exe).Length -lt 5MB) { throw 'Ejecutable publicado ausente o incompleto' }
$info = [System.Diagnostics.ProcessStartInfo]::new($exe)
$info.WorkingDirectory = $Root
$info.Arguments = '--self-test' # constante; compatible también con Windows PowerShell 5.1
$info.UseShellExecute = $false
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
$info.CreateNoWindow = $true
$process = [System.Diagnostics.Process]::Start($info)
try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(30000)) {
        $process.Kill() # el self-test no crea procesos hijos
        throw 'Timeout al arrancar el ejecutable publicado'
    }
    if ($process.ExitCode -ne 0 -or $stdout.Result -notmatch 'SYSDIAG_SELF_TEST_OK') {
        throw "Falló el ejecutable publicado: $($stderr.Result) $($stdout.Result)"
    }
    Write-Host $stdout.Result.Trim()
    Write-Host "Paquete validado mediante arranque real, carga de recursos WPF, JSON y reglas: $exe"
}
finally { $process.Dispose() }
