# Comprueba que la documentacion y los gates digan lo mismo que el codigo.
#
# Por que existe: la misma clase de error volvio una y otra vez a mano. Un numero
# (pruebas, version) vive en cuatro sitios y uno se queda atras; un "N.M" apunta a
# un encabezado que ya no existe; el README nombra clases que se borraron. Ninguna
# compilacion lo ve, asi que este script lo mira en cada corrida del CI.
#
# Este archivo es solo ASCII a proposito: Windows PowerShell 5.1 (el que usa build.bat)
# lee un .ps1 sin BOM como ANSI, y el texto acentuado se rompe.
param([string]$Root = (Join-Path $PSScriptRoot '..'))
$ErrorActionPreference = 'Stop'

$errores = New-Object System.Collections.Generic.List[string]
$simboloSeccion = [string][char]0x00A7   # seccion (signo de seccion)
$menorIgual = [string][char]0x2265       # signo mayor o igual

function Leer([string]$relativa) {
    Get-Content -LiteralPath (Join-Path $Root $relativa) -Raw -Encoding UTF8
}

$readme = Leer 'README.md'
$csproj = Leer 'SysDiag.csproj'
$pruebas = Leer 'Tools\validate_tests.ps1'
$changelog = Leer 'CHANGELOG.md'

# 1. La version del README es la del proyecto.
$versionReadme = [regex]::Match($readme, '(?m)^# SysDiag (\d+\.\d+\.\d+)')
$versionProyecto = [regex]::Match($csproj, '<Version>(\d+\.\d+\.\d+)</Version>')
if (-not $versionReadme.Success -or -not $versionProyecto.Success) {
    $errores.Add('No se encuentra la version en README.md o en SysDiag.csproj')
} elseif ($versionReadme.Groups[1].Value -cne $versionProyecto.Groups[1].Value) {
    $errores.Add("README dice $($versionReadme.Groups[1].Value) y SysDiag.csproj dice $($versionProyecto.Groups[1].Value)")
}
$version = $versionProyecto.Groups[1].Value

# 2. El umbral del gate, el README y la ultima mencion del CHANGELOG coinciden.
$umbralMatch = [regex]::Match($pruebas, '\$total -lt (\d+)')
if (-not $umbralMatch.Success) {
    throw 'No se encuentra el umbral de pruebas en Tools/validate_tests.ps1'
}
$umbral = [int]$umbralMatch.Groups[1].Value
if (-not $readme.Contains("$menorIgual$umbral pruebas")) {
    $errores.Add("README.md no declara $menorIgual$umbral pruebas, que es el umbral del gate")
}
$mencionChangelog = [regex]::Match($changelog, 'Umbral de pruebas del CI: (\d+)')
if (-not $mencionChangelog.Success -or [int]$mencionChangelog.Groups[1].Value -ne $umbral) {
    $errores.Add("CHANGELOG.md (la mencion mas reciente) no coincide con el umbral $umbral del gate")
}

# 3. Las referencias a secciones de docs/ existen. Solo se verifican encabezados de
#    uno o dos niveles (N y N.M). Un tercer nivel (N.M.K) no tiene encabezado: se
#    escribe "N.M, item K", que si se puede comprobar.
$documentos = @{
    'MEJORAS'               = 'docs\MEJORAS.md'
    'AUDITORIA_2026-10-09'  = 'docs\AUDITORIA_2026-10-09.md'
    'AUDITORIA'             = 'docs\AUDITORIA.md'
    'HERRAMIENTAS_NUEVAS'   = 'docs\HERRAMIENTAS_NUEVAS.md'
    'DISENO'                = 'docs\DISENO.md'
}
$encabezados = @{}
foreach ($nombre in $documentos.Keys) {
    $texto = Leer $documentos[$nombre]
    $conjunto = New-Object System.Collections.Generic.HashSet[string]
    foreach ($m in [regex]::Matches($texto, '(?m)^#{2,4}\s+(\d+(?:\.\d+)*)[\.\s]')) {
        [void]$conjunto.Add($m.Groups[1].Value)
    }
    $encabezados[$nombre] = $conjunto
}
$patronRef = '(MEJORAS|AUDITORIA_2026-10-09|AUDITORIA|HERRAMIENTAS_NUEVAS|DISENO)(?:\.md)?`?\s*\u00A7\s*(\d+(?:\.\d+)*)'
$fuentes = Get-ChildItem -LiteralPath $Root -Recurse -File |
    Where-Object { $_.Extension -in '.cs', '.md', '.xaml', '.ps1', '.yml', '.iss', '.bat' -and
                   $_.FullName -notmatch '\\(\.git|obj|bin|publish|TestResults)\\' }
foreach ($archivo in $fuentes) {
    $texto = Get-Content -LiteralPath $archivo.FullName -Raw -Encoding UTF8
    $relativo = $archivo.FullName.Substring((Resolve-Path $Root).Path.Length + 1)
    foreach ($m in [regex]::Matches($texto, $patronRef)) {
        $doc = $m.Groups[1].Value
        $seccion = $m.Groups[2].Value
        if (($seccion -split '\.').Count -gt 2) {
            $errores.Add("$relativo cita $simboloSeccion$seccion de $doc: usa $simboloSeccion" + (($seccion -split '\.')[0..1] -join '.') + ', item N en lugar de tres niveles')
        } elseif (-not $encabezados[$doc].Contains($seccion)) {
            $errores.Add("$relativo cita $simboloSeccion$seccion de $doc, que no tiene ese encabezado")
        }
    }
}

# 4. Los nombres de tipos o archivos que el README menciona existen en el codigo.
$declarados = New-Object System.Collections.Generic.HashSet[string]
foreach ($archivo in Get-ChildItem -LiteralPath $Root -Recurse -Filter *.cs -File |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }) {
    $texto = Get-Content -LiteralPath $archivo.FullName -Raw -Encoding UTF8
    foreach ($m in [regex]::Matches($texto, '\b(?:class|interface|record|enum|struct)\s+(\w+)')) {
        [void]$declarados.Add($m.Groups[1].Value)
    }
}
$nombresDeArchivo = @(Get-ChildItem -LiteralPath $Root -Recurse -File |
    ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_.Name) })
$vistos = New-Object System.Collections.Generic.HashSet[string]
foreach ($m in [regex]::Matches($readme, '\b(\w+(?:Service|Module|Window|Engine|Builder|Store|Rules?|Catalog|Report|Diff))\b')) {
    $nombre = $m.Groups[1].Value
    if (-not $vistos.Add($nombre)) { continue }
    if (-not $declarados.Contains($nombre) -and $nombresDeArchivo -notcontains $nombre) {
        $errores.Add("README.md menciona $nombre, que no existe en el codigo")
    }
}

if ($errores.Count -gt 0) {
    foreach ($e in $errores) { Write-Output "::error title=Documentacion::$e" }
    throw "Documentacion incoherente: $($errores.Count) problema(s). Corrige lo marcado arriba."
}
Write-Output "::notice title=Documentacion::version $version, umbral $umbral, secciones y nombres verificados"
