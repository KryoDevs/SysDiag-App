# Comprueba que la documentacion y los gates digan lo mismo que el codigo.
#
# Por que existe: la misma clase de error volvio una y otra vez a mano. Un numero
# (pruebas, version) vive en cuatro sitios y uno se queda atras; un "N.M" apunta a
# un encabezado que ya no existe; el README nombra clases que se borraron. Ninguna
# compilacion lo ve, asi que este script lo mira en cada corrida del CI.
#
# Como informa: cada fase publica un aviso y cada problema un error de workflow
# ("::notice" / "::error"), que el runner convierte en anotaciones de la corrida.
# No es adorno: el log de una corrida fallida no siempre se puede abrir (ni
# descargar), y un mensaje que solo vive en el log es un mensaje que nadie lee.
# El texto completo va siempre tambien al log. Si el script se detiene por una
# excepcion, la anotacion dice el tipo de error y la linea.
#
# Este archivo es solo ASCII a proposito: Windows PowerShell 5.1 (el que usa build.bat)
# lee un .ps1 sin BOM como ANSI, y el texto acentuado se rompe.
param([string]$Root = (Join-Path $PSScriptRoot '..'))
$ErrorActionPreference = 'Stop'

# Aviso y Falla son los dos unicos canales que sobreviven a una corrida sin log.
function Aviso([string]$mensaje) {
    Write-Output "::notice title=Documentacion::$mensaje"
}
function Falla([string]$mensaje) {
    Write-Output "::error title=Documentacion::$mensaje"
}

$errores = New-Object System.Collections.Generic.List[string]
$simboloSeccion = [string][char]0x00A7   # seccion (signo de seccion)
$menorIgual = [string][char]0x2265       # signo mayor o igual

function Leer([string]$relativa) {
    Get-Content -LiteralPath (Join-Path $Root $relativa) -Raw -Encoding UTF8
}

try {

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
    Aviso "fase 1: README $($versionReadme.Groups[1].Value), proyecto $version"

    # 2. El umbral del gate, el README y la ultima mencion del CHANGELOG coinciden.
    $umbralMatch = [regex]::Match($pruebas, '\$total -lt (\d+)')
    $umbral = 0
    $mencionChangelog = ''
    if (-not $umbralMatch.Success) {
        $errores.Add('No se encuentra el umbral de pruebas en Tools\validate_tests.ps1')
    } else {
        $umbral = [int]$umbralMatch.Groups[1].Value
        if (-not $readme.Contains("$menorIgual$umbral pruebas")) {
            $errores.Add("README.md no declara $menorIgual$umbral pruebas, que es el umbral del gate")
        }
        $coincidencia = [regex]::Match($changelog, 'Umbral de pruebas del CI: (\d+)')
        if ($coincidencia.Success) { $mencionChangelog = $coincidencia.Groups[1].Value }
        if (-not $coincidencia.Success -or [int]$mencionChangelog -ne $umbral) {
            $errores.Add("CHANGELOG.md (la mencion mas reciente) no coincide con el umbral $umbral del gate")
        }
    }
    Aviso "fase 2: umbral $umbral, CHANGELOG dice '$mencionChangelog'"

    # 3. Las referencias a secciones de docs/ existen. Solo se verifican encabezados de
    #    uno o dos niveles (N y N.M). Un tercer nivel (N.M.K) no tiene encabezado: se
    #    escribe "N.M, item K", que si se puede comprobar.
    $documentos = @{
        'MEJORAS'               = 'docs\MEJORAS.md'
        'AUDITORIA_2026-10-11'  = 'docs\AUDITORIA_2026-10-11.md'
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
    $patronRef = '(MEJORAS|AUDITORIA_2026-10-11|AUDITORIA_2026-10-09|AUDITORIA|HERRAMIENTAS_NUEVAS|DISENO)(?:\.md)?`?\s*\u00A7\s*(\d+(?:\.\d+)*)'
    $fuentes = Get-ChildItem -LiteralPath $Root -Recurse -File |
        Where-Object { $_.Extension -in '.cs', '.md', '.xaml', '.ps1', '.yml', '.iss', '.bat' -and
                       $_.FullName -notmatch '\\(\.git|obj|bin|publish|TestResults)\\' }
    $citas = 0
    $revisados = 0
    foreach ($archivo in $fuentes) {
        $revisados++
        $texto = Get-Content -LiteralPath $archivo.FullName -Raw -Encoding UTF8
        $relativo = $archivo.FullName.Substring((Resolve-Path $Root).Path.Length + 1)
        foreach ($m in [regex]::Matches($texto, $patronRef)) {
            $citas++
            $doc = $m.Groups[1].Value
            $seccion = $m.Groups[2].Value
            if (($seccion -split '\.').Count -gt 2) {
                # ${doc} y no $doc: dentro de comillas dobles, un signo de dos puntos pegado
                # al nombre se lee como variable con ambito y Windows PowerShell 5.1 lo
                # tolera mientras que PowerShell 7 lo rechaza en el analisis sintactico,
                # que es ANTES de ejecutar la primera comprobacion. Ese detalle dejo esta
                # puerta en rojo en el CI tres corridas seguidas con un simple "exit code 1".
                $errores.Add("$relativo cita $simboloSeccion$seccion de ${doc}: usa $simboloSeccion" + (($seccion -split '\.')[0..1] -join '.') + ', item N en lugar de tres niveles')
            } elseif (-not $encabezados[$doc].Contains($seccion)) {
                $errores.Add("$relativo cita $simboloSeccion$seccion de $doc, que no tiene ese encabezado")
            }
        }
    }
    Aviso "fase 3: $citas referencias a secciones en $revisados archivos"

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
    Aviso "fase 4: $($vistos.Count) nombres del README contra $($declarados.Count) tipos declarados"

    # 5. El instalador y su puerta buscan lo mismo. installer\SysDiag.iss declara
    #    donde deja el setup y Tools\validate_installer.ps1 donde lo busca: si uno
    #    cambia sin el otro, la puerta falla DESPUES de compilar el setup. Paso:
    #    Inno escribia en Output\ (su valor por defecto) y la puerta miraba la
    #    carpeta del .iss, asi que el job del instalador fallaba con "se esperaba
    #    un instalador y hay 0" sobre una compilacion correcta.
    $iss = Leer 'installer\SysDiag.iss'
    $puertaInstalador = Leer 'Tools\validate_installer.ps1'
    $salida = [regex]::Match($iss, '(?m)^\s*OutputDir\s*=\s*(.+?)\s*$')
    if (-not $salida.Success) {
        $errores.Add('installer\SysDiag.iss no declara OutputDir: la puerta del instalador no tiene ruta que comprobar')
    } else {
        $valor = $salida.Groups[1].Value.Trim()
        $conSourcePath = [regex]::Match($valor, '^\s*\{#SourcePath\}\s*(.*)$')
        if (-not $conSourcePath.Success) {
            $errores.Add("installer\SysDiag.iss usa OutputDir=$valor, que esta comprobacion no sabe leer: usa {#SourcePath}\<carpeta>")
        } else {
            $cola = ($conSourcePath.Groups[1].Value -replace '^[\\/]+', '').Trim()
            $carpetaSalida = if ($cola.Length -gt 0) { "installer\$cola" } else { 'installer' }
            if ($puertaInstalador -notmatch [regex]::Escape("$carpetaSalida\SysDiag-Setup")) {
                $errores.Add("Tools\validate_installer.ps1 no busca el setup en $carpetaSalida, que es el OutputDir de installer\SysDiag.iss")
            }
            Aviso "fase 5: el setup se compila en $carpetaSalida y la puerta lo busca ahi"
        }
    }

} catch {
    # Una excepcion no puede quedarse en el log: sin esto, la corrida dice
    # "exit code 1" y nada mas, que es exactamente el mensaje inutil que este
    # proyecto ya se comio una vez.
    Falla ("la puerta se detuvo en la linea " + $_.InvocationInfo.ScriptLineNumber + ": " + $_.Exception.GetType().Name + ": " + $_.Exception.Message)
    exit 1
}

if ($errores.Count -gt 0) {
    Write-Output ''
    Write-Output "Documentacion incoherente: $($errores.Count) problema(s)."
    foreach ($e in $errores) { Write-Output "  $e" }
    # Las anotaciones de un paso tienen tope: se anotan las primeras y el resto
    # queda en el log, que ya se imprime completo arriba.
    foreach ($e in @($errores | Select-Object -First 8)) { Falla $e }
    if ($errores.Count -gt 8) {
        Falla "$($errores.Count) problemas en total: arriba estan los primeros 8, el resto queda en el log del paso"
    }
    exit 1
}
Aviso "version $version, umbral $umbral, secciones y nombres verificados"
exit 0
