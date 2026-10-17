param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$packageRoot = (Resolve-Path -LiteralPath $PackageDirectory).Path
$manifest = Get-Content (Join-Path $packageRoot 'manifest.json') -Raw | ConvertFrom-Json
$setup = Join-Path $packageRoot ($manifest.files | Where-Object variant -eq 'win-x64-setup').name
$portable = Join-Path $packageRoot ($manifest.files | Where-Object variant -eq 'win-x64').name
$armSetup = Join-Path $packageRoot ($manifest.files | Where-Object variant -eq 'win-arm64-setup').name
$installRoot = Join-Path $packageRoot 'installer-smoke/installed'
$dataRoot = Join-Path $packageRoot 'installer-smoke/user-data'
$registryPath = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/Jemur'
$programGroup = Join-Path ([Environment]::GetFolderPath('Programs')) 'Jemur'
$runPath = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Run'
$originalRun = (Get-ItemProperty $runPath -Name Jemur -ErrorAction SilentlyContinue).Jemur
if (Test-Path $registryPath) { throw 'An existing Jemur installation is registered; smoke test will not overwrite it.' }
if (Test-Path 'HKCU:/Software/Jemur') { throw 'An existing Jemur installer key is present.' }
if (Test-Path $programGroup) { throw 'An existing Jemur Start menu group is present.' }
if (Test-Path $installRoot) { throw 'Smoke install directory already exists.' }
New-Item -ItemType Directory -Force -Path (Join-Path $dataRoot 'data') | Out-Null
$sentinel = Join-Path $dataRoot 'data/preserve-me.txt'
'Preserve user data during uninstall.' | Set-Content $sentinel

function Invoke-Setup([string]$Path, [string[]]$Arguments, [int]$Expected = 0) {
    $process = Start-Process -FilePath $Path -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Installer process timed out.' }
    if ($process.ExitCode -ne $Expected) { throw "Unexpected installer exit code: $($process.ExitCode); expected $Expected" }
}

function Test-Render([string]$Exe, [string]$Label) {
    $previousData = $env:JEMUR_DATA
    try {
        $env:JEMUR_DATA = $dataRoot
        $renderRoot = Join-Path $packageRoot "installer-smoke/rendered-$Label"
        $process = Start-Process -FilePath $Exe -ArgumentList @('--render-docs', $renderRoot) -WindowStyle Hidden -PassThru
        if (-not $process.WaitForExit(45000)) { $process.Kill(); throw 'WPF render timed out.' }
        if ($process.ExitCode -ne 0) { throw "WPF render failed: $($process.ExitCode)" }
        if (@(Get-ChildItem $renderRoot -Filter '*.png').Count -ne 8) { throw 'Expected eight rendered WPF pages.' }
        Write-Output "WPF render verified: $Label (8 pages)"
    } finally { $env:JEMUR_DATA = $previousData }
}

Test-Render $portable 'portable'
Invoke-Setup $armSetup @('/S', "/D=$installRoot-arm64") 1633
if (Test-Path "$installRoot-arm64") { throw 'ARM64 installer created files on an x64 OS.' }
Write-Output 'ARM64 architecture rejection verified on x64.'

try {
    Invoke-Setup $setup @('/S', "/D=$installRoot")
    $installedExe = Join-Path $installRoot 'Jemur.exe'
    if ((Get-FileHash $installedExe).Hash -ne (Get-FileHash $portable).Hash) { throw 'Installed payload differs from portable EXE.' }
    $registration = Get-ItemProperty $registryPath
    if ($registration.DisplayVersion -ne $manifest.version -or $registration.InstallLocation -ne $installRoot) { throw 'Incorrect installed-apps registration.' }
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut((Join-Path $programGroup 'Jemur.lnk'))
    if ($shortcut.TargetPath -ne $installedExe) { throw 'Start menu shortcut has wrong target.' }
    Write-Output 'Installer payload, version registration and Start menu shortcut verified.'
    Test-Render $installedExe 'installed'

    Invoke-Setup $setup @('/S', "/D=$installRoot")
    if ((Get-FileHash $installedExe).Hash -ne (Get-FileHash $portable).Hash) { throw 'Upgrade changed the payload unexpectedly.' }
    Write-Output 'Reinstallation/upgrade verified.'

    $appMutex = [Threading.Mutex]::new($false, 'Jemur.SingleInstance.v1')
    try {
        Invoke-Setup $setup @('/S', "/D=$installRoot") 1618
        Invoke-Setup (Join-Path $installRoot 'Uninstall.exe') @('/S', "_?=$installRoot") 1618
        if (-not (Test-Path $installedExe)) { throw 'Running-app guard removed installed files.' }
        Write-Output 'Running-app guards verified for install and uninstall.'
    } finally { $appMutex.Dispose() }

    if (-not $originalRun) {
        New-Item -Path $runPath -Force | Out-Null
        New-ItemProperty -Path $runPath -Name Jemur -Value ('"' + $installedExe + '" --background') -PropertyType String -Force | Out-Null
    }
} finally {
    $uninstaller = Join-Path $installRoot 'Uninstall.exe'
    if (Test-Path $uninstaller) {
        Invoke-Setup $uninstaller @('/S')
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        while ((Test-Path $registryPath) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
    }
}
if (Test-Path (Join-Path $installRoot 'Jemur.exe')) { throw 'Uninstall left Jemur.exe behind.' }
if (Test-Path $registryPath) { throw 'Uninstall left registration behind.' }
if (Test-Path $programGroup) { throw 'Uninstall left Start menu shortcuts behind.' }
if (-not (Test-Path $sentinel)) { throw 'Uninstall deleted user data.' }
$remainingRun = (Get-ItemProperty $runPath -Name Jemur -ErrorAction SilentlyContinue).Jemur
if ($remainingRun -ne $originalRun) { throw 'Startup entry cleanup did not preserve the original state.' }
Write-Output 'Uninstall, startup cleanup and user-data preservation verified.'
Write-Output "Windows installer smoke passed for $($manifest.version), commit $($manifest.commit)"
