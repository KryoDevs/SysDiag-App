# validate_xaml.ps1
#
# Comprobaciones estáticas sobre el XAML. No sustituyen al autotest del EXE
# publicado (--self-test): lo complementan. Ese construye las ventanas, pero
# las plantillas de control y los Storyboards de sus triggers solo se
# materializan cuando el usuario pasa el cursor por encima. Un TargetName mal
# escrito o un {StaticResource} que no existe no rompe la construcción de la
# ventana: rompe el primer hover, que es exactamente el sitio donde nadie mira.
#
#   1. Bien formado. El error típico al retocar plantillas es cerrar
#      </Trigger.ExitActions> un <Trigger.EnterActions>: parsea en el editor y
#      revienta en WPF.
#   2. Claves de recurso. Cada {StaticResource K} de una vista tiene que estar
#      en Ui/Theme.xaml, en App.xaml o en su propio diccionario.
#   3. Ámbito de nombres. Todo TargetName (de Setter y de Storyboard) tiene que
#      apuntar a un x:Name de la MISMA plantilla.
#
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\validate_xaml.ps1
# Devuelve 0 si todo pasa, 1 si algo falla.

param([string]$Root = ".")

$uiDir = Join-Path $Root "Ui"
$theme = Join-Path $uiDir "Theme.xaml"
$app = Join-Path $Root "App.xaml"
if (-not (Test-Path $theme)) { Write-Error "No se encontró $theme (ejecutar desde la raíz)"; exit 2 }

$archivos = @(Get-ChildItem $uiDir -Filter *.xaml -File | ForEach-Object { $_.FullName }) + $app

$globales = [System.Collections.Generic.HashSet[string]]::new()
foreach ($f in @($theme, $app)) {
    if (-not (Test-Path $f)) { continue }
    foreach ($m in [regex]::Matches((Get-Content $f -Raw), 'x:Key="([^"]+)"')) {
        [void]$globales.Add($m.Groups[1].Value)
    }
}

$errores = @()
$analizados = 0

foreach ($archivo in $archivos) {
    $nombre = Split-Path $archivo -Leaf
    $texto = Get-Content $archivo -Raw

    # 1) bien formado
    $xml = New-Object System.Xml.XmlDocument
    $xml.XmlResolver = $null
    try { $xml.LoadXml($texto); $analizados++ }
    catch { $errores += "$nombre : XML mal formado -> $($_.Exception.Message)"; continue }

    # 2) claves de recurso
    $locales = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($m in [regex]::Matches($texto, 'x:Key="([^"]+)"')) { [void]$locales.Add($m.Groups[1].Value) }
    foreach ($m in [regex]::Matches($texto, '\{StaticResource\s+([^}\s]+)\}')) {
        $k = $m.Groups[1].Value
        if (-not $globales.Contains($k) -and -not $locales.Contains($k)) {
            $errores += "$nombre : StaticResource inexistente -> $k"
        }
    }

    # 3) TargetName dentro de su plantilla
    foreach ($m in [regex]::Matches($texto, '(?s)<(ControlTemplate|DataTemplate)\b.*?</\1>')) {
        $bloque = $m.Value
        $names = [System.Collections.Generic.HashSet[string]]::new()
        foreach ($n in [regex]::Matches($bloque, 'x:Name="([^"]+)"')) { [void]$names.Add($n.Groups[1].Value) }
        foreach ($t in [regex]::Matches($bloque, '(?:Storyboard\.)?TargetName="([^"]+)"')) {
            if (-not $names.Contains($t.Groups[1].Value)) {
                $linea = ($texto.Substring(0, $m.Index + $t.Index) -split "`n").Count
                $errores += "${nombre}:$linea : TargetName '$($t.Groups[1].Value)' no está declarado en su plantilla"
            }
        }
    }
}

if ($errores.Count -gt 0) {
    Write-Host "validate_xaml: $($errores.Count) problema(s) en $analizados archivo(s)."
    foreach ($e in ($errores | Select-Object -Unique)) { Write-Host "  $e" }
    exit 1
}

Write-Host "validate_xaml: OK ($analizados archivos XAML, $($globales.Count) claves globales)."
exit 0
