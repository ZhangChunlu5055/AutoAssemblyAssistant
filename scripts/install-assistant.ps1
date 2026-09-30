$ErrorActionPreference = 'Stop'
try {
    if (Get-Process -Name SLDWORKS -ErrorAction SilentlyContinue) {
        throw 'Save your work and close ALL SolidWorks windows before installing. No process will be terminated.'
    }
    $assemblyPath = Join-Path $PSScriptRoot 'AutoAssemblyAddin.dll'
    if (-not (Test-Path -LiteralPath $assemblyPath)) { throw 'Run install.bat from the release-assistant package directory.' }
    foreach ($name in @('SolidWorks.Interop.sldworks.dll', 'SolidWorks.Interop.swconst.dll', 'SolidWorks.Interop.swpublished.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $name))) { throw "Missing dependency: $name" }
    }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"'))
        $process = Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -PassThru -Wait
        if ($process.ExitCode -ne 0) { throw 'Administrator installation failed or was cancelled. See install-error.txt if present.' }
        Write-Host 'Installation completed. Restart SolidWorks and enable Auto Assembly Pose in Tools > Add-Ins.'
        exit 0
    }
    $regasm = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'
    & $regasm $assemblyPath /codebase
    if ($LASTEXITCODE -ne 0) { throw 'COM registration failed.' }
    $guid = '{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}'
    $addinKey = "HKLM:\SOFTWARE\SolidWorks\AddIns\$guid"
    $startupKey = "HKCU:\Software\SolidWorks\AddInsStartup\$guid"
    New-Item -Path $addinKey -Force | Out-Null
    New-ItemProperty -Path $addinKey -Name '(default)' -Value 0 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $addinKey -Name 'Title' -Value 'Auto Assembly Pose' -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $addinKey -Name 'Description' -Value 'Task pane assistant, module replacement and pose tools' -PropertyType String -Force | Out-Null
    New-Item -Path $startupKey -Force | Out-Null
    New-ItemProperty -Path $startupKey -Name '(default)' -Value 1 -PropertyType DWord -Force | Out-Null
    Write-Host 'Installation completed. Keep this package directory in its current location.'
    exit 0
}
catch {
    $_.Exception.Message | Out-File -LiteralPath (Join-Path $PSScriptRoot 'install-error.txt') -Encoding UTF8
    Write-Error $_.Exception.Message -ErrorAction Continue
    exit 1
}
