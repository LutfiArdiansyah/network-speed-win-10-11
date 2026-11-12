#Requires -RunAsAdministrator
<#
.SYNOPSIS
  THROWAWAY SPIKE (Plan 1 Task 7). Mencari hak MINIMAL agar akun LOCAL SERVICE bisa membaca ETW
  Microsoft-Windows-Kernel-Network (spec S-03), lalu mengukur akurasinya (S-05, S-09).

.DESCRIPTION
  Dijalankan oleh PENGGUNA di PowerShell "Run as administrator".
  Spike berjalan lewat Task Scheduler dengan akun service (bukan Windows Service sungguhan)
  agar tidak butuh paket NuGet tambahan; hak akses token-nya sama dengan service.

  -Run : publish spike, lalu bertahap (berhenti di tahap pertama yang berhasil):
           A0  LOCAL SERVICE apa adanya                         (2 menit)
           A1  + anggota Performance Log Users                  (2 menit, hanya bila A0 gagal)
           A2  + izin enable provider Kernel-Network            (2 menit, hanya bila A1 gagal)
         lalu pengukuran utama B dengan konfigurasi minimal tadi (-MinutesB, default 30),
         lalu C LocalSystem sebagai pembanding                  (2 menit).
  -Uninstall : hentikan & hapus task, cabut izin provider dan keanggotaan grup yang DITAMBAHKAN skrip ini.
               Log tetap disimpan kecuali -RemoveLogs.
  Keduanya boleh dipakai bersamaan (-Uninstall -Run): bersihkan dulu, lalu jalankan ulang.
#>
param(
    [switch]$Run,
    [switch]$Uninstall,
    [int]$MinutesB = 30,
    [switch]$RemoveLogs
)

$ErrorActionPreference = 'Stop'
$Root = Join-Path $env:ProgramData 'NetworkSpeedSpike'
$Bin = Join-Path $Root 'bin'
$Exe = Join-Path $Bin 'EtwSpike.exe'
$StatePath = Join-Path $Root 'install-state.json'
$TaskPath = '\NetworkSpeedSpike\'
$LocalService = 'NT AUTHORITY\LOCALSERVICE'
$LocalServiceSid = 'S-1-5-19'
$PerfLogUsersSid = 'S-1-5-32-559'

function Get-State {
    if (Test-Path $StatePath) { return Get-Content $StatePath -Raw | ConvertFrom-Json }
    return [pscustomobject]@{ AddedToPerfLogUsers = $false; ProviderGranted = $false }
}

function Save-State($state) { $state | ConvertTo-Json | Set-Content -Path $StatePath -Encoding utf8 }

function Test-LocalServiceInPerfLogUsers {
    $members = Get-LocalGroupMember -SID $PerfLogUsersSid -ErrorAction SilentlyContinue
    return [bool]($members | Where-Object { $_.SID.Value -eq $LocalServiceSid })
}

function Invoke-Variant([string]$Name, [string]$UserId, [int]$Minutes) {
    $taskName = "EtwSpike-$Name"
    $startedAt = Get-Date
    $action = New-ScheduledTaskAction -Execute $Exe -Argument "--minutes $Minutes --out `"$Root`"" -WorkingDirectory $Bin
    $principal = New-ScheduledTaskPrincipal -UserId $UserId -LogonType ServiceAccount -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes ($Minutes + 5)) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
    Register-ScheduledTask -TaskPath $TaskPath -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Force | Out-Null
    Write-Host "[$Name] start as $UserId for $Minutes minute(s) at $($startedAt.ToString('HH:mm:ss'))..."
    Start-ScheduledTask -TaskPath $TaskPath -TaskName $taskName
    Start-Sleep -Seconds 5
    while ((Get-ScheduledTask -TaskPath $TaskPath -TaskName $taskName).State -eq 'Running') { Start-Sleep -Seconds 5 }
    $info = Get-ScheduledTaskInfo -TaskPath $TaskPath -TaskName $taskName
    $log = Get-ChildItem $Root -Filter 'etw-*.log' | Where-Object { $_.LastWriteTime -ge $startedAt } | Sort-Object LastWriteTime | Select-Object -Last 1
    $ok = $log -and (Select-String -Path $log.FullName -Pattern 'SESSION-OK' -Quiet)
    Write-Host "[$Name] finished: LastTaskResult=$($info.LastTaskResult) sessionOk=$ok log=$($log.Name)"
    return [bool]$ok
}

if ($Uninstall) {
    $state = Get-State
    Get-ScheduledTask -TaskPath $TaskPath -ErrorAction SilentlyContinue | ForEach-Object {
        Stop-ScheduledTask -TaskPath $TaskPath -TaskName $_.TaskName -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskPath $TaskPath -TaskName $_.TaskName -Confirm:$false
    }
    Get-Process -Name EtwSpike -ErrorAction SilentlyContinue | Stop-Process -Force
    logman stop NetworkSpeedSpike-Etw -ets 2>$null | Out-Null
    if ($state.ProviderGranted -and (Test-Path $Exe)) {
        # Sebelum spike tidak ada security descriptor khusus untuk provider ini, jadi menghapusnya = kembali ke default.
        & $Exe --remove-provider-access
    }
    if ($state.AddedToPerfLogUsers) {
        Remove-LocalGroupMember -SID $PerfLogUsersSid -Member $LocalServiceSid -ErrorAction SilentlyContinue
        Write-Host 'LOCAL SERVICE removed from Performance Log Users.'
    }
    Remove-Item -Recurse -Force $Bin -ErrorAction SilentlyContinue
    Remove-Item -Force $StatePath -ErrorAction SilentlyContinue
    if ($RemoveLogs) { Remove-Item -Recurse -Force $Root -ErrorAction SilentlyContinue }
    Write-Host 'Uninstall selesai.'
}

if ($Run) {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    New-Item -ItemType Directory -Force -Path $Bin | Out-Null
    # Seluruh output sesi admin dicatat agar hasil/error bisa dibaca dari luar jendela admin.
    Start-Transcript -Path (Join-Path $Root "run-transcript-$(Get-Date -Format yyyyMMdd-HHmmss).txt") | Out-Null
    try {
        # LOCAL SERVICE perlu menulis log ke folder ini.
        icacls $Root /grant "*${LocalServiceSid}:(OI)(CI)M" | Out-Null

        # Jendela admin dimulai di System32; pindah ke root repo agar global.json & NuGet config repo terpakai.
        Push-Location $repoRoot
        Write-Host "Publishing spike from $repoRoot ..."
        # PS 5.1: stderr native + 2>&1 + ErrorAction Stop akan melempar NativeCommandError; longgarkan sementara.
        $ErrorActionPreference = 'Continue'
        $publishOutput = & "$env:ProgramFiles\dotnet\dotnet.exe" publish (Join-Path $repoRoot 'spikes\EtwSpike') -c Release -o $Bin --nologo 2>&1
        $publishExit = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        Pop-Location
        $publishOutput | ForEach-Object { Write-Host "  $_" }
        if ($publishExit -ne 0 -or -not (Test-Path $Exe)) { throw "Publish failed (exit $publishExit). Lihat output di atas." }

        $state = Get-State
        $config = 'A0 LocalService (default)'
        $ok = Invoke-Variant -Name 'A0-LocalService' -UserId $LocalService -Minutes 2

        if (-not $ok) {
            if (-not (Test-LocalServiceInPerfLogUsers)) {
                Add-LocalGroupMember -SID $PerfLogUsersSid -Member $LocalServiceSid
                $state.AddedToPerfLogUsers = $true
                Save-State $state
                Write-Host 'LOCAL SERVICE added to Performance Log Users.'
            }
            $config = 'A1 LocalService + Performance Log Users'
            $ok = Invoke-Variant -Name 'A1-LocalService-PLU' -UserId $LocalService -Minutes 2
        }

        if (-not $ok) {
            & $Exe --grant-provider-access $LocalServiceSid
            if ($LASTEXITCODE -eq 0) { $state.ProviderGranted = $true; Save-State $state }
            $config = 'A2 LocalService + PLU + provider grant'
            $ok = Invoke-Variant -Name 'A2-LocalService-Grant' -UserId $LocalService -Minutes 2
        }

        Write-Host ">>> Konfigurasi minimal: $config (berhasil=$ok)"
        Write-Host '>>> Selama varian B berjalan: unduh file besar ±5 menit, buka Windows Update > Check for updates, lalu biarkan idle.'
        Invoke-Variant -Name 'B-Main' -UserId $LocalService -Minutes $MinutesB | Out-Null
        Invoke-Variant -Name 'C-LocalSystem' -UserId 'NT AUTHORITY\SYSTEM' -Minutes 2 | Out-Null

        Write-Host "Selesai. Log ada di $Root (etw-*.log). Jalankan -Uninstall untuk membersihkan."
    }
    finally {
        Stop-Transcript | Out-Null
    }
}

if (-not $Run -and -not $Uninstall) { Get-Help $MyInvocation.MyCommand.Path -Detailed }
