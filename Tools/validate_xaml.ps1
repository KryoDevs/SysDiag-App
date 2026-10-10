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
#   4. La propiedad animada existe en el tipo del destino. WPF no avisa nunca: un
#      TargetProperty que el tipo no tiene simplemente no anima, sin excepción y
#      sin traza. Es la clase de error que dos veces quedó vivo en este proyecto.
#   5. RepeatBehavior="Forever" dentro de un Trigger tiene que poder pararse.
#   6. Los miembros enlazados existen. Un `{Binding Etiqueta}` cuyo nombre no
#      está en el ViewModel no rompe la compilación: rompe en tiempo de
#      ejecución, en silencio y solo en la ventana de salida. Es el hueco más
#      grande que le quedaba a esta puerta, y también el más fácil de dejar
#      pasar porque el editor lo subraya y la compilación no dice nada.
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


# ---- tablas de la comprobación 4 -----------------------------------------
#
# Propiedad -> tipos de elemento que la tienen. $null significa "válida en
# cualquier elemento" (Opacity vive en UIElement). La tabla está escrita a mano
# y no consultada por reflexión porque el paso corre en pwsh, donde cargar
# PresentationFramework para enumerar propiedades es más frágil que lo que aporta.
# Si el tema empieza a animar otra propiedad, se agrega acá; una propiedad que no
# está en la tabla no se comprueba (lista blanca de lo verificado, no lista negra).
$animables = @{
    Opacity          = $null
    ScaleX           = @('ScaleTransform')
    ScaleY           = @('ScaleTransform')
    X                = @('TranslateTransform')
    Y                = @('TranslateTransform')
    Angle            = @('RotateTransform')
    StrokeDashOffset = @('Path', 'Line', 'Polyline', 'Rectangle', 'Ellipse', 'Shape')
}

# Y el revés de Opacity, que como no tiene tipo único necesita la suya: estos
# objetos sí aceptan un SetValue sin quejarse, pero no se pintan, así que animarles
# la opacidad es un no-op silencioso.
$sinOpacidad = @(
    'ScaleTransform', 'TranslateTransform', 'RotateTransform', 'MatrixTransform',
    'TransformGroup', 'SolidColorBrush', 'LinearGradientBrush', 'DropShadowEffect'
)

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

    # 3) TargetName dentro de su plantilla, 4) propiedad contra tipo,
    #    5) los forever parables
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

        # 4) el tipo del destino tiene que tener la propiedad que se anima
        $tipos = @{}
        foreach ($d in [regex]::Matches($bloque, '<(\w+)[^>]*?x:Name="([^"]+)"')) { $tipos[$d.Groups[2].Value] = $d.Groups[1].Value }

        # Cubre también las variantes *UsingKeyFrames*: la propiedad animada vive
        # en el propio elemento de la animación, así que hay que mirarla igual.
        foreach ($a in [regex]::Matches($bloque, '<\w*Animation\w*\b[^>]*?>')) {
            $attrs = $a.Value
            $prop = [regex]::Match($attrs, 'Storyboard\.TargetProperty="([^"]+)"')
            $nom = [regex]::Match($attrs, 'Storyboard\.TargetName="([^"]+)"')
            if (-not $prop.Success -or -not $nom.Success) { continue }
            $p = $prop.Groups[1].Value
            if ($p -match '[\(\.]') { continue }   # rutas compuestas: no se resuelven acá
            $destino = $nom.Groups[1].Value
            $tipo = $tipos[$destino]
            if (-not $tipo) { continue }
            if ($p -eq 'Opacity') {
                if ($sinOpacidad -contains $tipo) {
                    $linea = ($texto.Substring(0, $m.Index + $a.Index) -split "`n").Count
                    $errores += "${nombre}:$linea : se anima Opacity sobre $tipo ('$destino'), que no se pinta: el resultado es una animación que no hace nada"
                }
                continue
            }
            if (-not $animables.ContainsKey($p)) { continue }
            $permitidos = $animables[$p]
            if ($permitidos -and $permitidos -notcontains $tipo) {
                $linea = ($texto.Substring(0, $m.Index + $a.Index) -split "`n").Count
                $errores += "${nombre}:$linea : '$destino' es $tipo y se le anima $p, que ese tipo no tiene (WPF no avisa: no anima y ya)"
            }
        }

        # 5) un forever que nace en un trigger tiene que morir con ese estado.
        #    Los que viven en un EventTrigger no entran: su horizonte es el
        #    elemento, y WPF los suelta al descargarlo.
        foreach ($ent in [regex]::Matches($bloque, '(?s)<Trigger\.EnterActions>(.*?)</Trigger\.EnterActions>')) {
            $interior = $ent.Groups[1].Value
            if ($interior -notmatch 'RepeatBehavior="Forever"') { continue }
            foreach ($bs in [regex]::Matches($interior, '<BeginStoryboard([^>]*)>')) {
                $bsNombre = [regex]::Match($bs.Groups[1].Value, 'x:Name="([^"]+)"')
                if (-not $bsNombre.Success) {
                    $errores += "${nombre}: BeginStoryboard con RepeatBehavior=Forever dentro de EnterActions y sin x:Name: nadie lo puede parar"
                    continue
                }
                if ($bloque -notmatch "BeginStoryboardName=`"$($bsNombre.Groups[1].Value)`"") {
                    $errores += "${nombre}: Forever en EnterActions sin StopStoryboard para '$($bsNombre.Groups[1].Value)': la animación sigue corriendo cuando el estado que la disparó ya terminó"
                }
            }
        }
    }
}

# ---- tablas de la comprobación 6 -----------------------------------------
#
# Vista -> clase fuente donde viven los miembros que se enlazan. Escrita a
# mano: la alternativa es cargar el ensamblado por reflexión en pwsh, que es
# más frágil que una tabla de diez líneas que se lee de un vistazo.
$origenesDatos = @{
    'MainWindow.xaml'    = 'Ui\MainViewModel.cs'
    'HistoryWindow.xaml' = 'Ui\HistoryWindow.xaml.cs'
    'SmartWindow.xaml'    = 'Ui\SmartWindow.xaml.cs'
    'ConsumoWindow.xaml'  = 'Ui\ConsumoWindow.xaml.cs'
}

# Nombres que se enlazan y no son miembros del ViewModel: son propiedades de
# los objetos que viajan dentro de las plantillas (un Finding, una fila de
# tabla), de otro elemento por ElementName, o del propio WPF. Lista blanca
# explícita: si un binding nuevo cae acá, se agrega a mano y con su razón.
$enlacesExternos = @(
    'IsChecked',        # ElementName de los chips de filtro
    'Content', 'Visibility', 'Opacity', 'Text', 'IsEnabled', 'ToolTip',  # WPF / ElementName
    'Etiqueta', 'Message', 'Severity', 'Tooltip', 'Detalle',             # Finding y filas
    'X', 'Y', 'YTexto', 'Arco', 'Area', 'Color', 'ValorTexto', 'Porcentaje',  # gráficos
    'Pista', 'AnchoPista', 'MarcaX', 'MarcaVisible', 'Texto', 'Fraccion',     # gráficos
    'Ancho', 'Alto', 'Fill', 'Stroke', 'Points', 'Data',                     # formas
    'Clave', 'Valor', 'Categoria', 'Titulo', 'Descripcion', 'Prioridad',     # filas y recomendaciones
    'Id', 'Nombre', 'Riesgo', 'NotaAplicacion'
)

foreach ($archivo in $archivos) {
    $nombre = Split-Path $archivo -Leaf
    if (-not $origenesDatos.ContainsKey($nombre)) { continue }

    $rutaOrigen = Join-Path $Root ($origenesDatos[$nombre])
    if (-not (Test-Path $rutaOrigen)) {
        $errores += "$nombre : la puerta declara $($origenesDatos[$nombre]) como origen de datos y ese archivo no existe"
        continue
    }

    # Miembros públicos de la clase: `public Tipo Nombre` y `public Tipo Nombre {`.
    # Sin parsear C#: alcanza para propiedades y campos, que es lo que se enlaza.
    $fuente = Get-Content $rutaOrigen -Raw
    $miembros = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($m in [regex]::Matches($fuente, 'public\s+(?:static\s+|readonly\s+|const\s+)*[\w<>,\.\[\]\?\(\)]+\s+(\w+)\s*(?:\{|=>|;)')) {
        [void]$miembros.Add($m.Groups[1].Value)
    }

    $texto = Get-Content $archivo -Raw
    foreach ($b in [regex]::Matches($texto, '\{Binding\s+([^},]+)')) {
        $ruta = $b.Groups[1].Value.Trim()
        $raiz = ($ruta -split '\.')[0].Trim()
        if ($raiz.Length -eq 0) { continue }
        if ($enlacesExternos -contains $raiz) { continue }
        if ($raiz.StartsWith('(') -or $raiz.StartsWith('/')) { continue }  # rutas compuestas y RelativeSource
        if (-not $miembros.Contains($raiz)) {
            $linea = ($texto.Substring(0, $b.Index) -split "`n").Count
            $errores += "${nombre}:${linea} : {Binding $raiz} no es un miembro de $($origenesDatos[$nombre]) (WPF no avisa: el enlace falla en silencio y la vista queda vacía)"
        }
    }
}

if ($errores.Count -gt 0) {
    Write-Host "validate_xaml: $($errores.Count) problema(s) en $analizados archivo(s)."
    foreach ($e in ($errores | Select-Object -Unique)) { Write-Host "  $e" }
    exit 1
}

Write-Host "validate_xaml: OK ($analizados archivos XAML, $($globales.Count) claves globales, plantillas y storyboards coherentes)."
exit 0
