param([string]$SolidWorksInteropDir, [switch]$SkipBuild, [ValidatePattern('^release-assistant(?:-[0-9.]+)?$')][string]$PackageDirectoryName = 'release-assistant-1.3.1')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) {
    $buildArguments = @()
    if ($SolidWorksInteropDir) { $buildArguments += "-p:SolidWorksInteropDir=$SolidWorksInteropDir" }
    & dotnet build (Join-Path $projectRoot 'src\AutoAssemblyAddin\AutoAssemblyAddin.csproj') -c Release -p:Platform=x64 @buildArguments
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
$sourceDirectory = Join-Path $projectRoot 'src\AutoAssemblyAddin\bin\x64\Release\net48'
$packageDirectory = Join-Path $projectRoot $PackageDirectoryName
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
foreach ($name in @('AutoAssemblyAddin.dll', 'SolidWorks.Interop.sldworks.dll', 'SolidWorks.Interop.swconst.dll', 'SolidWorks.Interop.swpublished.dll')) {
    Copy-Item -LiteralPath (Join-Path $sourceDirectory $name) -Destination (Join-Path $packageDirectory $name) -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install-assistant.ps1') -Destination (Join-Path $packageDirectory 'install-assistant.ps1') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install-assistant.bat') -Destination (Join-Path $packageDirectory 'install.bat') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\assistant-usage.md') -Destination (Join-Path $packageDirectory 'README.md') -Force
Get-ChildItem -LiteralPath $packageDirectory -Filter '*.dll' | Get-FileHash -Algorithm SHA256 |
    Select-Object @{Name='File';Expression={Split-Path $_.Path -Leaf}}, Hash |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packageDirectory 'checksums.json') -Encoding UTF8
Write-Host "Assistant package ready: $packageDirectory"
