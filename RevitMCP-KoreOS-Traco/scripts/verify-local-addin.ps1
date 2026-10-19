# Default expectation is the version staged by the manifest (ProductVersion minus "+commit").
param([string]$ExpectedVersion = '')
$ErrorActionPreference = 'Stop'
# Read-only: never submits a modeling command or accepts a Revit dialog.
$baseUri = 'http://127.0.0.1:7891'
$addinRoot = Join-Path $env:APPDATA 'Autodesk/Revit/Addins/2026'
[xml]$manifest = Get-Content -LiteralPath (Join-Path $addinRoot 'RevitMCPAddin.addin') -Raw
$assemblyPath = $manifest.SelectSingleNode('/RevitAddIns/AddIn/Assembly').InnerText
if (!(Test-Path -LiteralPath $assemblyPath)) { throw 'Manifest references a missing assembly.' }
$stagedVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyPath).ProductVersion
if (!$ExpectedVersion) { $ExpectedVersion = ($stagedVersion -split '\+')[0] }
$health = Invoke-RestMethod -Uri "$baseUri/health" -TimeoutSec 5
$report = [ordered]@{
    expectedVersion = $ExpectedVersion
    loadedVersion = $health.version
    stagedVersion = $stagedVersion
    assemblyPath = $assemblyPath
    buildTimestampUtc = $health.buildTimestampUtc
    commandCount = $health.commandCount
    readOnly = $true
    status = 'restart_required'
}
if ($health.version -ne $ExpectedVersion) {
    $report | ConvertTo-Json -Depth 8
    exit 2
}
$token = (Get-Content -LiteralPath (Join-Path $addinRoot 'revit-mcp-token.txt') -Raw).Trim()
$headers = @{ Authorization = "Bearer $token" }
$body = @{ command = 'get_warnings'; params = @{ offset = 0; limit = 20 } } | ConvertTo-Json
$response = Invoke-RestMethod -Uri "$baseUri/mcp" -Method Post -Headers $headers -ContentType 'application/json' -Body $body -TimeoutSec 30
if (!$response.ok) {
    $report.status = 'command_failed'
    $report.error = $response.error
    $report | ConvertTo-Json -Depth 8
    exit 3
}
$report.status = 'read_only_checks_passed'
$report.warnings = $response.data
$report.commitFailureHandlingValidated = $false
$report | ConvertTo-Json -Depth 12
