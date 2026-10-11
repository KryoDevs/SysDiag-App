# Ejecuta una puerta de Tools\ en un proceso aparte y publica su diagnostico.
#
# Por que existe: un paso del CI que corre una puerta y falla dice, en el resumen,
# "Process completed with exit code 1" y nada mas. Si el motivo vive en el log, y
# el log de una corrida fallida no siempre se puede abrir (ni descargar), el
# diagnostico se pierde. Paso: la puerta de documentacion tuvo el CI en rojo tres
# corridas seguidas sin decir por que, porque el archivo no analizaba y el error
# de analisis solo existia en el log.
#
# Que hace:
#   1. Corre el script en un proceso hijo y guarda su salida completa (la normal y
#      la de error) en un archivo temporal.
#   2. Imprime esa salida, para que quede en el log.
#   3. La repite como anotaciones de la corrida, que se leen desde la API de GitHub
#      sin abrir el log. Las lineas que ya son comandos de workflow ("::error",
#      "::notice") las convierte el runner con solo imprimirlas.
#   4. Con -Comentar, y si la corrida es de un PR, deja el texto como comentario.
#      Si el comentario no se puede publicar, lo dice en una anotacion.
#   5. Sale con el mismo codigo de salida que la puerta.
#
# Uso: pwsh -NoProfile -File .\Tools\run_gate.ps1 -Script .\Tools\validate_docs.ps1 -Comentar
#
# Nota: capturar la salida de error de un proceso nativo necesita PowerShell 7
# (en Windows PowerShell 5.1, la redireccion de todas las corrientes no la
# incluye). El CI usa PowerShell 7; para Windows PowerShell 5.1 quedan las puertas
# que build.bat llama directamente.

param(
    [Parameter(Mandatory = $true)][string]$Script,
    [string[]]$Argumentos = @(),
    [switch]$Comentar
)

$ErrorActionPreference = 'Continue'   # el error del hijo se informa; no cuelga el lanzador

function Anotacion([string]$mensaje) {
    Write-Output "::error title=Puerta::$mensaje"
}

$carpeta = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
$registro = Join-Path $carpeta ('puerta-' + [System.IO.Path]::GetFileNameWithoutExtension($Script) + '.log')

$codigo = 1
$salida = ''
try {
    $lineaDeComando = @('-NoProfile', '-File', $Script) + $Argumentos
    & pwsh @lineaDeComando *> $registro
    $codigo = $LASTEXITCODE
    if (Test-Path -LiteralPath $registro) { $salida = Get-Content -LiteralPath $registro -Raw }
} catch {
    Anotacion ('no se pudo ejecutar ' + $Script + ': ' + $_.Exception.Message)
    exit 2
}
if ($null -eq $salida) { $salida = '' }
$salida = ($salida -replace "`r", '').Trim()
if ($salida.Length -gt 6500) { $salida = $salida.Substring(0, 6500) + ' ...' }
Write-Output $salida

if ($codigo -eq 0) { exit 0 }

if ($salida.Length -eq 0) {
    Anotacion ('el script ' + $Script + ' terminó con codigo ' + $codigo + ' sin escribir nada: el problema es anterior a la puerta')
}
# Una anotacion por linea del diagnostico, para no depender del log. El tope de
# anotaciones por paso es chico: se anotan las primeras lineas utiles.
foreach ($linea in @($salida -split "`n" | Where-Object { $_.Trim().Length -gt 0 } | Select-Object -First 8)) {
    if ($linea.Trim().StartsWith('::')) { continue }   # ya lo proceso el runner al imprimirlo
    Anotacion $linea.Trim()
}

if ($Comentar -and $env:PR) {
    $sha = ''
    if ($env:GITHUB_SHA) { $sha = $env:GITHUB_SHA.Substring(0, 7) }
    $uri = $env:GITHUB_API_URL + '/repos/' + $env:GITHUB_REPOSITORY + '/issues/' + $env:PR + '/comments'
    $cabeceras = @{ Authorization = 'Bearer ' + $env:GH_TOKEN; Accept = 'application/vnd.github+json' }
    $grave = [string][char]0x60
    $cercado = $grave + $grave + $grave
    $primeraLinea = 'La puerta ' + [System.IO.Path]::GetFileName($Script) + ' falló en ' + $sha
    try {
        $previos = Invoke-RestMethod -Uri $uri -Headers $cabeceras
        if (@($previos | Where-Object { $_.body -like ($primeraLinea + '*') }).Count -gt 0) {
            Write-Output '::notice title=Puerta::ya hay un comentario de esta revision; no se duplica'
        } else {
            $cuerpo = $primeraLinea + ' (codigo ' + $codigo + ').' + "`n`n" + $cercado + "text`n" + $salida + "`n" + $cercado
            $json = @{ body = $cuerpo } | ConvertTo-Json -Depth 2
            Invoke-RestMethod -Method Post -Uri $uri -Headers $cabeceras -ContentType 'application/json; charset=utf-8' -Body ([System.Text.Encoding]::UTF8.GetBytes($json)) | Out-Null
            Write-Output '::notice title=Puerta::el motivo quedo como comentario del PR'
        }
    } catch {
        $motivo = ($_.Exception.Message -replace "`r?`n", ' ')
        Anotacion ('no se pudo publicar el comentario del PR: ' + $motivo)
    }
}
exit $codigo
