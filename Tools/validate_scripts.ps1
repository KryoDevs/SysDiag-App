# Los scripts del repositorio tienen que poder ANALIZARSE antes de que alguien los
# ejecute, y tienen que leerse bien en las dos versiones de PowerShell que los usan.
#
# Por que existe: la puerta de documentacion tuvo el CI en rojo tres corridas
# seguidas por una sola linea. Un signo de dos puntos pegado a un nombre de
# variable dentro de comillas dobles ("$doc: texto") se lee como variable con
# ambito: Windows PowerShell 5.1 lo toleraba, PowerShell 7 lo rechaza en el
# analisis sintactico. Un error de analisis mata el archivo ENTERO antes de
# ejecutar la primera comprobacion, asi que la puerta no comprobaba nada y el CI
# solo decia "exit code 1": el mensaje vivia en el log, que no siempre se puede
# abrir. Esta puerta lo dice con archivo, linea y motivo, y como anotacion.
#
#   1. Sintaxis: cada .ps1 se analiza con el mismo analizador que lo va a ejecutar
#      (el de PowerShell 7, que es el del CI). Si no analiza, no corre.
#   2. Codificacion: un .ps1 con caracteres no ASCII y sin BOM lo lee Windows
#      PowerShell 5.1 (el que usa build.bat) como ANSI y el texto se rompe. O el
#      archivo es ASCII, o lleva BOM. Es la convencion que documenta la cabecera
#      de Tools/validate_docs.ps1.
#
# Uso: pwsh -NoProfile -File .\Tools\validate_scripts.ps1 -Root .
# Devuelve 0 si todo pasa, 1 si algo falla.

param([string]$Root = (Join-Path $PSScriptRoot '..'))
$ErrorActionPreference = 'Stop'

function Falla([string]$mensaje) {
    Write-Output "::error title=Scripts::$mensaje"
}

$problemas = 0
$analizados = 0
$conBom = 0

$archivos = @(Get-ChildItem -LiteralPath $Root -Recurse -Filter *.ps1 -File |
    Where-Object { $_.FullName -notmatch '\\(obj|bin|publish|release|TestResults)\\' } |
    Sort-Object -Property FullName)

foreach ($archivo in $archivos) {
    $analizados++
    $relativo = $archivo.Name
    try { $relativo = $archivo.FullName.Substring((Resolve-Path $Root).Path.Length + 1) } catch { }

    # 1. Sintaxis.
    $tokens = $null
    $erroresDeSintaxis = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($archivo.FullName, [ref]$tokens, [ref]$erroresDeSintaxis)
    foreach ($e in @($erroresDeSintaxis)) {
        $problemas++
        $mensaje = ($e.Message -replace '\r?\n', ' ')
        Falla ($relativo + '(' + $e.Extent.StartLineNumber + ',' + $e.Extent.StartColumnNumber + '): ' + $mensaje)
        $culpable = ''
        try { $culpable = @(Get-Content -LiteralPath $archivo.FullName)[$e.Extent.StartLineNumber - 1] } catch { }
        if ($culpable.Length -gt 0) { Write-Output ('    ' + $culpable.Trim()) }
    }

    # 2. Codificacion.
    $bytes = [System.IO.File]::ReadAllBytes($archivo.FullName)
    $tieneBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    if ($tieneBom) { $conBom++ }
    $noAscii = $false
    foreach ($b in $bytes) {
        if ($b -gt 0x7F) { $noAscii = $true; break }
    }
    if ($noAscii -and -not $tieneBom) {
        $problemas++
        Falla ($relativo + ': tiene caracteres no ASCII y no lleva BOM; Windows PowerShell 5.1 lo lee como ANSI y el texto se rompe')
    }
}

if ($problemas -gt 0) {
    Falla ("$problemas problema(s) de sintaxis o codificacion en $analizados script(s)")
    exit 1
}
Write-Output "::notice title=Scripts::$analizados script(s) se analizan con PowerShell $($PSVersionTable.PSVersion); $conBom llevan BOM"
exit 0
