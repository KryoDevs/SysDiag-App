#Requires -Version 5.1
<#
.SYNOPSIS
    Emite (y verifica) códigos de activación para SysDiag.

.DESCRIPTION
    Este script es la contraparte de Core/Licensing/LicenseCrypto.cs y usa
    exactamente el mismo formato:

        8 bytes de carga útil + 10 bytes de HMAC-SHA256 truncado,
        codificados en base32 Crockford y mostrados como SDG7-AAAAA-...-EEEE

    Carga útil: [0] versión, [1] banderas (bit0 = vinculada al equipo,
    bit1 = perpetua), [2..3] expiración (días desde 2026-01-01, 0 = perpetua),
    [4..7] serial.

    La clave HMAC está en este archivo y en la aplicación a propósito: es un
    sistema de disuasión para reventa informal, no una caja fuerte. Guárdalo
    fuera del repositorio público.

.EXAMPLE
    .\New-ActivationCode.ps1 -Dias 365 -Cantidad 5
    Emite 5 códigos con un año de vigencia.

.EXAMPLE
    .\New-ActivationCode.ps1 -Dias 0 -VinculadaEquipo
    Emite un código perpetuo, válido solo en esta máquina.

.EXAMPLE
    .\New-ActivationCode.ps1 -Verificar "SDG7-ABCDE-FGHJK-MNPQR-STVWX-YZA0B"
    Comprueba un código y muestra su contenido.
#>
param(
    # Días de vigencia desde hoy. 0 = perpetuo.
    [int]$Dias = 365,
    # El código solo funcionará en el equipo donde se emite.
    [switch]$VinculadaEquipo,
    # Serial interno; si se omite, uno aleatorio por código.
    [uint32]$Serial = 0,
    # Cuántos códigos emitir.
    [int]$Cantidad = 1,
    # En lugar de emitir, verifica un código existente.
    [string]$Verificar = ''
)

$ErrorActionPreference = 'Stop'

$Alfabeto = '0123456789ABCDEFGHJKMNPQRSTVWXYZ'
$Origen = [datetime]::new(2026, 1, 1)

function Get-Clave {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return , $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes('SysDiag/2026/clave-privada-v1')) }
    finally { $sha.Dispose() }
}

function Get-EtiquetaEquipo {
    $texto = ($env:COMPUTERNAME + '|' + $env:USERNAME).ToUpperInvariant()
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return , $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($texto)) }
    finally { $sha.Dispose() }
}

function Get-Mac([byte[]]$Carga, [bool]$Vinculada) {
    $entrada = New-Object System.Collections.Generic.List[byte]
    $entrada.AddRange($Carga)
    if ($Vinculada) { $entrada.AddRange((Get-EtiquetaEquipo)) }
    $hmac = New-Object System.Security.Cryptography.HMACSHA256
    try {
        $hmac.Key = Get-Clave
        $resumen = $hmac.ComputeHash($entrada.ToArray())
    }
    finally { $hmac.Dispose() }
    $mac = New-Object byte[] 10
    [Array]::Copy($resumen, $mac, 10)
    return , $mac
}

function ConvertTo-Base32([byte[]]$Datos) {
    $sb = New-Object System.Text.StringBuilder
    [int64]$buffer = 0
    $bits = 0
    foreach ($b in $Datos) {
        $buffer = ($buffer -shl 8) -bor $b
        $bits += 8
        while ($bits -ge 5) {
            $bits -= 5
            [void]$sb.Append($Alfabeto[[int](($buffer -shr $bits) -band 31)])
        }
    }
    if ($bits -gt 0) { [void]$sb.Append($Alfabeto[[int](($buffer -shl (5 - $bits)) -band 31)]) }
    return $sb.ToString()
}

function ConvertFrom-Base32([string]$Texto) {
    $salida = New-Object System.Collections.Generic.List[byte]
    [int64]$buffer = 0
    $bits = 0
    foreach ($c in $Texto.ToCharArray()) {
        $u = [char]::ToUpperInvariant($c)
        if ($u -eq 'O') { $u = '0' }
        elseif ($u -eq 'I' -or $u -eq 'L') { $u = '1' }
        $idx = $Alfabeto.IndexOf($u)
        if ($idx -lt 0) { throw "Carácter no válido en el código: $c" }
        $buffer = ($buffer -shl 5) -bor $idx
        $bits += 5
        if ($bits -ge 8) {
            $bits -= 8
            $salida.Add([byte](($buffer -shr $bits) -band 0xFF))
        }
    }
    return , $salida.ToArray()
}

function Formatear([string]$Base32) {
    $grupos = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $Base32.Length; $i += 5) {
        $grupos.Add($Base32.Substring($i, [Math]::Min(5, $Base32.Length - $i)))
    }
    return 'SDG7-' + ($grupos -join '-')
}

function New-CodigoUnico([int]$Dias, [bool]$Vinculada, [uint32]$Serial) {
    if ($Dias -lt 0 -or $Dias -gt 65534) { throw 'La vigencia va de 0 a 65534 días.' }

    if ($Dias -eq 0) { $expira = 0 } else {
        $expira = [int]([datetime]::Today - $Origen).Days + $Dias
        if ($expira -gt 65534) { throw 'La fecha de expiración excede el formato.' }
    }

    $carga = New-Object byte[] 8
    $carga[0] = 1
    $banderas = 0
    if ($Vinculada) { $banderas = $banderas -bor 1 }
    if ($Dias -eq 0) { $banderas = $banderas -bor 2 }
    $carga[1] = [byte]$banderas
    $carga[2] = [byte]($expira -band 0xFF)
    $carga[3] = [byte](($expira -shr 8) -band 0xFF)
    $carga[4] = [byte]($Serial -band 0xFF)
    $carga[5] = [byte](($Serial -shr 8) -band 0xFF)
    $carga[6] = [byte](($Serial -shr 16) -band 0xFF)
    $carga[7] = [byte](($Serial -shr 24) -band 0xFF)

    $mac = Get-Mac $carga $Vinculada
    $paquete = New-Object byte[] 18
    [Array]::Copy($carga, 0, $paquete, 0, 8)
    [Array]::Copy($mac, 0, $paquete, 8, 10)
    return Formatear (ConvertTo-Base32 $paquete)
}

# ---- Modo verificación --------------------------------------------------

if ($Verificar -ne '') {
    $limpio = ($Verificar.ToUpperInvariant() -replace '[\s\-]', '')
    if ($limpio.StartsWith('SDG7')) { $limpio = $limpio.Substring(4) }
    $paquete = ConvertFrom-Base32 $limpio
    if ($paquete.Count -ne 18) { throw 'El código está incompleto.' }

    $carga = $paquete[0..7]
    $mac = $paquete[8..17]
    if ($carga[0] -ne 1) { throw 'Código de una versión no compatible.' }

    $vinculada = ($carga[1] -band 1) -ne 0
    $perpetua = ($carga[1] -band 2) -ne 0
    $esperado = Get-Mac $carga $vinculada

    $valido = $true
    for ($i = 0; $i -lt 10; $i++) { if ($mac[$i] -ne $esperado[$i]) { $valido = $false } }
    if (-not $valido) { throw 'El código no es válido (la firma no coincide).' }

    $dias = $carga[2] -bor ($carga[3] -shl 8)
    # Se calcula en int64: con int32, un serial con el bit 31 puesto daría
    # negativo y el casteo a uint32 fallaría en PowerShell 5.1.
    $ser = [uint32](([int64]$carga[4]) -bor ([int64]$carga[5] -shl 8) -bor ([int64]$carga[6] -shl 16) -bor ([int64]$carga[7] -shl 24))
    $expiraTexto = if ($perpetua -or $dias -eq 0) { 'perpetuo' } else { $Origen.AddDays($dias).ToString('yyyy-MM-dd') }

    Write-Host "Código VÁLIDO"
    Write-Host "  Vigencia : $expiraTexto"
    Write-Host "  Serial   : $ser"
    Write-Host "  Vínculo  : $(if ($vinculada) { 'este equipo' } else { 'cualquier equipo' })"
    exit 0
}

# ---- Modo emisión -------------------------------------------------------

if ($Cantidad -lt 1 -or $Cantidad -gt 1000) { throw '-Cantidad va de 1 a 1000.' }

for ($n = 0; $n -lt $Cantidad; $n++) {
    $ser = $Serial
    if ($ser -eq 0) { $ser = [uint32](Get-Random -Minimum 1 -Maximum [int]::MaxValue) }
    Write-Output (New-CodigoUnico $Dias ([bool]$VinculadaEquipo) $ser)
}
