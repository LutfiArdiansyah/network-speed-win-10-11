<#
.SYNOPSIS
  Menandatangani file dengan sertifikat code signing Network Speed (Authenticode SHA-256 + timestamp RFC 3161, spec 9).

.DESCRIPTION
  - Password .pfx dari environment variable NETWORKSPEED_SIGN_PASSWORD, atau prompt bila tidak ada.
  - .pfx di dalam repo ditolak.
  - Exit code 0 bila semua file tertanda tangan oleh sertifikat ini dan ber-timestamp; 1 bila ada yang gagal.
  - Status "UnknownError" (root self-signed belum dipercaya di PC build) diterima selama signer & timestamp benar.

.EXAMPLE
  $env:NETWORKSPEED_SIGN_PASSWORD = '...'
  powershell -ExecutionPolicy Bypass -File tools\signing\Sign-Files.ps1 -PfxPath D:\secure\NetworkSpeedCodeSigning.pfx -Files a.exe,b.dll
#>
param(
    [Parameter(Mandatory = $true)][string]$PfxPath,
    [Parameter(Mandatory = $true)][string[]]$Files,
    [string]$TimestampServer = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SigningCommon.ps1')

# "powershell -File ... -Files a.exe,b.dll" mengirim satu string berisi koma (PS 5.1); pecah agar kedua cara pemanggilan sama.
$Files = @($Files | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

Assert-OutsideRepo -Path $PfxPath -What 'PfxPath'
if (-not (Test-Path -LiteralPath $PfxPath -PathType Leaf)) { throw "PFX not found: $PfxPath" }

if ($env:NETWORKSPEED_SIGN_PASSWORD) {
    $password = ConvertTo-SecureString $env:NETWORKSPEED_SIGN_PASSWORD -AsPlainText -Force
}
else {
    $password = Read-Host -AsSecureString 'PFX password'
}

$flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::UserKeySet
$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($PfxPath, $password, $flags)
try {
    if (-not $cert.HasPrivateKey) { throw 'The PFX does not contain a private key.' }
    $codeSigningOid = '1.3.6.1.5.5.7.3.3'
    $eku = $cert.Extensions | Where-Object { $_ -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] }
    if (-not ($eku -and ($eku.EnhancedKeyUsages | Where-Object { $_.Value -eq $codeSigningOid }))) {
        throw 'The certificate is not a code signing certificate.'
    }
    if ($cert.NotAfter -lt (Get-Date)) { throw "The certificate expired on $($cert.NotAfter.ToString('yyyy-MM-dd'))." }

    $failed = 0
    foreach ($file in $Files) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
            Write-Warning "Not found: $file"
            $failed++
            continue
        }
        $result = Set-AuthenticodeSignature -LiteralPath $file -Certificate $cert -HashAlgorithm SHA256 -TimestampServer $TimestampServer
        $signer = $result.SignerCertificate
        $ok = ($result.Status -eq 'Valid' -or $result.Status -eq 'UnknownError') `
            -and $signer -and $signer.Thumbprint -eq $cert.Thumbprint `
            -and $result.TimeStamperCertificate
        if ($ok) {
            Write-Host "Signed: $file"
        }
        else {
            Write-Warning "Signing failed: $file ($($result.Status): $($result.StatusMessage))"
            $failed++
        }
    }
}
finally {
    $cert.Dispose()
}

if ($failed -gt 0) {
    Write-Error "$failed file(s) could not be signed."
    exit 1
}
exit 0
