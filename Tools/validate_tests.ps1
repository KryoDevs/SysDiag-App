param([string]$Root = (Join-Path $PSScriptRoot '..\TestResults'))
$ErrorActionPreference = 'Stop'
$files = @(Get-ChildItem $Root -Filter '*.trx')
if ($files.Count -eq 0) { throw 'No se generó ningún resultado TRX' }
$total = 0; $passed = 0; $failed = 0
foreach ($file in $files) {
    [xml]$document = Get-Content $file.FullName -Raw
    foreach ($result in $document.TestRun.Results.UnitTestResult) {
        if ($result.outcome -eq 'Passed') { continue }
        $detail = "$($result.testName): $($result.Output.ErrorInfo.Message) $($result.Output.ErrorInfo.StackTrace)"
        $detail = $detail.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
        Write-Output "::error title=Regresión $($result.outcome)::$detail"
    }
    $counters = $document.TestRun.ResultSummary.Counters
    $total += [int]$counters.total; $passed += [int]$counters.passed; $failed += [int]$counters.failed
}
if ($total -lt 113 -or $passed -ne $total -or $failed -gt 0) {
    throw "Suite incompleta o fallida: $passed/$total aprobadas, $failed fallidas; mínimo 113 (suite auditada: no eliminar pruebas sin reemplazarlas)"
}
Write-Output "::notice title=Pruebas de regresión::$passed/$total pruebas aprobadas, sin omisiones ni fallos"
