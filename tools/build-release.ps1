<#
.SYNOPSIS
  Membuat rilis Network Speed: test -> publish self-contained -> sign -> installer Inno Setup -> latest.json (Plan 5).

.DESCRIPTION
  Output di artifacts\release:
    NetworkSpeedSetup-<Version>.exe   installer (bertanda tangan bila -PfxPath diberikan)
    latest.json                       untuk shared folder update (spec 10)
  Tanpa -PfxPath build tetap jalan tetapi installer TIDAK bertanda tangan (hanya untuk uji).
  Password .pfx: environment variable NETWORKSPEED_SIGN_PASSWORD, atau prompt.
  Butuh: .NET 10 SDK, Inno Setup 6. Publish self-contained mengunduh runtime pack dari NuGet bila belum ada di cache.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\build-release.ps1 -Version 1.0.0 -PfxPath D:\secure\NetworkSpeedCodeSigning.pfx
#>
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d{1,9}\.\d{1,9}\.\d{1,9}$')][string]$Version,
    [string]$PfxPath,
    [ValidateLength(0, 500)][string]$Notes = '',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
$signScript = Join-Path $PSScriptRoot 'signing\Sign-Files.ps1'
$publishDir = Join-Path $repo 'artifacts\publish'
$releaseDir = Join-Path $repo 'artifacts\release'

function Invoke-Checked {
    param([Parameter(Mandatory = $true)][string]$Exe, [Parameter(Mandatory = $true)][string[]]$Arguments)
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$(Split-Path $Exe -Leaf) failed with exit code $LASTEXITCODE." }
}

foreach ($tool in $dotnet, $iscc) {
    if (-not (Test-Path -LiteralPath $tool)) { throw "Required tool not found: $tool" }
}
$signing = [bool]$PfxPath
if ($signing) {
    . (Join-Path $PSScriptRoot 'signing\SigningCommon.ps1')
    Assert-OutsideRepo -Path $PfxPath -What 'PfxPath'
    if (-not $env:NETWORKSPEED_SIGN_PASSWORD) {
        # Satu prompt untuk semua pemanggilan Sign-Files (termasuk dari ISCC); hanya untuk proses ini.
        $env:NETWORKSPEED_SIGN_PASSWORD = ConvertFrom-SecureStringPlain (Read-Host -AsSecureString 'PFX password')
        $clearPassword = $true
    }
}

try {
    Push-Location $repo
    if (-not $SkipTests) {
        Invoke-Checked $dotnet @('test')
    }
    Invoke-Checked $dotnet @('build', '-c', 'Release', '-warnaserror', "-p:Version=$Version")

    Remove-Item -Recurse -Force $publishDir, $releaseDir -ErrorAction SilentlyContinue
    $projects = @{ app = 'src\NetworkSpeed.App'; service = 'src\NetworkSpeed.Service' }
    foreach ($name in $projects.Keys) {
        Invoke-Checked $dotnet @('publish', $projects[$name], '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
            "-p:Version=$Version", '-o', (Join-Path $publishDir $name), '--nologo')
    }

    if ($signing) {
        $own = Get-ChildItem -Path $publishDir -Recurse -File -Include 'NetworkSpeed.*.exe', 'NetworkSpeed.*.dll' | ForEach-Object { $_.FullName }
        & $signScript -PfxPath $PfxPath -Files $own
        if ($LASTEXITCODE -ne 0) { throw 'Signing the published files failed.' }
    }
    else {
        Write-Warning 'UNSIGNED build: no -PfxPath given. Do not distribute this installer.'
    }

    $isccArgs = @("/DAppVersion=$Version", "/DPublishDir=$publishDir", "/DOutputDir=$releaseDir", '/Qp')
    if ($signing) {
        $psExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $isccArgs += '/DSignToolName=nssign'
        $isccArgs += "/Snssign=`$q$psExe`$q -NoProfile -ExecutionPolicy Bypass -File `$q$signScript`$q -PfxPath `$q$PfxPath`$q -Files `$f"
    }
    Invoke-Checked $iscc ($isccArgs + (Join-Path $repo 'installer\NetworkSpeed.iss'))

    $setup = Join-Path $releaseDir "NetworkSpeedSetup-$Version.exe"
    if (-not (Test-Path -LiteralPath $setup)) { throw "Installer not produced: $setup" }

    # latest.json mengikuti aturan validasi App (Core UpdateManifest, Plan 5 Task 1).
    $hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    $installerName = Split-Path $setup -Leaf
    if ($installerName -notmatch '^[A-Za-z0-9_-][A-Za-z0-9._-]{0,99}\.exe$' -or $hash -notmatch '^[0-9a-f]{64}$') {
        throw 'Generated manifest values are invalid.'
    }
    $manifest = [ordered]@{ version = $Version; installer = $installerName; sha256 = $hash; notes = $Notes }
    $json = $manifest | ConvertTo-Json
    $manifestPath = Join-Path $releaseDir 'latest.json'
    [System.IO.File]::WriteAllText($manifestPath, $json, (New-Object System.Text.UTF8Encoding $false))
    if ((Get-Item -LiteralPath $manifestPath).Length -gt 16KB) { throw 'latest.json is larger than 16 KB.' }

    Write-Host ''
    Write-Host "Release $Version ready in $releaseDir"
    Write-Host "  Installer : $installerName"
    Write-Host "  SHA-256   : $hash"
    Write-Host "  Signed    : $signing"
}
finally {
    Pop-Location
    if ($clearPassword) { Remove-Item Env:NETWORKSPEED_SIGN_PASSWORD -ErrorAction SilentlyContinue }
}
