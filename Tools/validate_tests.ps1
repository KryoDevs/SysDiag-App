param([string]$Root = (Join-Path $PSScriptRoot '..\TestResults'))
$ErrorActionPreference = 'Stop'
$files = @(Get-ChildItem $Root -Filter '*.trx')
if ($files.Count -eq 0) { throw 'No se generó ningún resultado TRX' }
$total = 0; $passed = 0; $failed = 0
foreach ($file in $files) {
    [xml]$document = Get-Content $file.FullName -Raw
    $counters = $document.TestRun.ResultSummary.Counters
    $total += [int]$counters.total; $passed += [int]$counters.passed; $failed += [int]$counters.failed
}
if ($total -lt 80 -or $passed -ne $total -or $failed -gt 0) {
    throw "Suite incompleta o fallida: $passed/$total aprobadas, $failed fallidas; mínimo 80"
}
Write-Output "::notice title=Pruebas de regresión::$passed/$total pruebas aprobadas, sin omisiones ni fallos"
