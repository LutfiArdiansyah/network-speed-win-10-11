#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Pasang/lepas NetworkSpeedService untuk PENGEMBANGAN (Plan 3, Task 10). Installer produksi dibuat di Plan 5.

.DESCRIPTION
  Dijalankan oleh PENGGUNA di PowerShell "Run as administrator" dari root repo.

  -Install   : publish service ke %ProgramData%\NetworkSpeed\dev-bin, siapkan folder data dengan ACL (spec H-05:
               tulis hanya LocalService/SYSTEM/Administrators; user biasa tanpa akses), daftarkan service
               "NetworkSpeedService" (akun NT AUTHORITY\LocalService, start otomatis, restart saat crash 1 mnt × 3, S-07),
               lalu start.
  -Uninstall : stop & hapus service dan dev-bin. Data (history.db, log) tetap kecuali -RemoveData.

  Perubahan sistem: service NetworkSpeedService, folder %ProgramData%\NetworkSpeed (dev-bin, data, ACL).
  Tidak ada perubahan grup atau security ETW (S-03: LocalService bawaan cukup).
#>
param(
    [switch]$Install,
    [switch]$Uninstall,
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
$ServiceName = 'NetworkSpeedService'
$Root = Join-Path $env:ProgramData 'NetworkSpeed'
$Bin = Join-Path $Root 'dev-bin'
$Exe = Join-Path $Bin 'NetworkSpeed.Service.exe'

function Remove-DevService {
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc) {
        if ($svc.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force; $svc.WaitForStatus('Stopped', '00:00:30') }
        sc.exe delete $ServiceName | Out-Null
        Write-Host "Service $ServiceName removed."
    }
}

if ($Uninstall) {
    Remove-DevService
    Remove-Item -Recurse -Force $Bin -ErrorAction SilentlyContinue
    if ($RemoveData) { Remove-Item -Recurse -Force $Root -ErrorAction SilentlyContinue; Write-Host 'Data removed.' }
    Write-Host 'Uninstall selesai.'
}

if ($Install) {
    New-Item -ItemType Directory -Force -Path $Root | Out-Null
    Start-Transcript -Path (Join-Path $Root "dev-transcript-$(Get-Date -Format yyyyMMdd-HHmmss).txt") | Out-Null
    try {
        Remove-DevService
        # User biasa boleh membuat folder di ProgramData; bila folder ini dibuat lebih dulu oleh user, dia menjadi owner
        # (hak WRITE_DAC implisit) dan bisa mengganti exe service. Ambil alih owner & ACL sebelum menaruh binari.
        icacls $Root /setowner '*S-1-5-32-544' /T /C | Out-Null
        icacls $Root /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-19:(OI)(CI)M' /T /C | Out-Null
        $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
        Push-Location $repoRoot
        Write-Host "Publishing service from $repoRoot ..."
        $ErrorActionPreference = 'Continue'
        $out = & "$env:ProgramFiles\dotnet\dotnet.exe" publish (Join-Path $repoRoot 'src\NetworkSpeed.Service') -c Release -o $Bin --nologo 2>&1
        $code = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        Pop-Location
        $out | ForEach-Object { Write-Host "  $_" }
        if ($code -ne 0 -or -not (Test-Path $Exe)) { throw "Publish failed (exit $code)." }

        # Spec H-05: data per PC hanya bisa ditulis LocalService/SYSTEM/Administrators; user biasa tidak punya akses.
        icacls $Root /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-19:(OI)(CI)M' | Out-Null
        # Binari dev: LocalService hanya perlu baca/eksekusi.
        icacls $Bin /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-19:(OI)(CI)RX' | Out-Null

        sc.exe create $ServiceName binPath= "`"$Exe`"" start= auto obj= 'NT AUTHORITY\LocalService' DisplayName= 'Network Speed' | Out-Null
        sc.exe description $ServiceName 'Records network usage history and per-app traffic for Network Speed.' | Out-Null
        sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
        Start-Service -Name $ServiceName
        (Get-Service -Name $ServiceName).WaitForStatus('Running', '00:00:30')
        Write-Host "Service $ServiceName running as NT AUTHORITY\LocalService."
        Write-Host 'Selesai. Verifikasi sebagai user biasa: dotnet run --project tools/ServiceProbe'
    }
    finally {
        Stop-Transcript | Out-Null
    }
}

if (-not $Install -and -not $Uninstall) { Get-Help $MyInvocation.MyCommand.Path -Detailed }
