$ErrorActionPreference = 'Stop'
function FileDigest([string]$path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($path)
    try { return [System.BitConverter]::ToString($sha.ComputeHash($stream)) }
    finally { $stream.Dispose(); $sha.Dispose() }
}
$repo = Split-Path $PSScriptRoot -Parent
$source = Join-Path $repo 'src/RevitAddin/bin/Release'
$addinRoot = Join-Path $env:APPDATA 'Autodesk/Revit/Addins/2026'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$destination = Join-Path $addinRoot "RevitMCP-local-$stamp"
$manifestPath = Join-Path $addinRoot 'RevitMCPAddin.addin'
$files = @('RevitMCPAddin.dll', 'RevitMCP.Core.dll', 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.Wpf.dll')
foreach ($file in $files) {
    if (!(Test-Path -LiteralPath (Join-Path $source $file))) { throw "Missing build file: $file" }
}
# A failed build leaves the previous DLL in bin/Release; refuse to stage a stale version.
[xml]$project = Get-Content -LiteralPath (Join-Path $repo 'src/RevitAddin/RevitMCPAddin.csproj') -Raw
$expectedVersion = @($project.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
$built = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $source 'RevitMCPAddin.dll')).FileVersion
if (-not $built.StartsWith($expectedVersion)) { throw "Stale build: RevitMCPAddin.dll is $built, csproj is $expectedVersion. Rebuild first." }
# RevitMCP.Core.dll carries no product version: require it to be newer than every Core source file.
$newestSource = Get-ChildItem -LiteralPath (Join-Path $repo 'src/RevitMCP.Core') -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ((Get-Item -LiteralPath (Join-Path $source 'RevitMCP.Core.dll')).LastWriteTime -lt $newestSource.LastWriteTime) {
    throw "Stale build: RevitMCP.Core.dll is older than $($newestSource.Name). Rebuild first."
}
$loader = Join-Path $source 'runtimes/win-x64/native/WebView2Loader.dll'
if (!(Test-Path -LiteralPath $loader)) { throw 'Missing native WebView2 loader' }
$null = New-Item -ItemType Directory -Path $destination
foreach ($file in $files) {
    Copy-Item -LiteralPath (Join-Path $source $file) -Destination $destination
    if ((FileDigest (Join-Path $source $file)) -ne
        (FileDigest (Join-Path $destination $file))) { throw "Hash mismatch: $file" }
}
Copy-Item -LiteralPath $loader -Destination $destination
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $destination 'manifest-before.xml')
[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
$assemblyNode = $manifest.SelectSingleNode('/RevitAddIns/AddIn/Assembly')
$assemblyNode.InnerText = [string][System.IO.Path]::Combine($destination, 'RevitMCPAddin.dll')
$manifest.Save($manifestPath)
Write-Output "Staged and verified: $destination"
Write-Output "Manifest updated: $manifestPath"
Write-Output 'Running Revit unchanged. Restart Revit to activate.'
