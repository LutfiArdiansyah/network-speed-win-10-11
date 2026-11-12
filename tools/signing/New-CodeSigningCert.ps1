<#
.SYNOPSIS
  Membuat sertifikat self-signed khusus code signing untuk Network Speed (spec 9).

.DESCRIPTION
  - EKU Code Signing saja, RSA 3072, SHA-256, berlaku -Years tahun (default 3).
  - Menulis <OutDir>\NetworkSpeedCodeSigning.pfx (private key, dilindungi password) dan
    <OutDir>\NetworkSpeedCodeSigning.cer (sertifikat publik untuk diimpor IT).
  - Sertifikat dihapus dari store CurrentUser\My setelah di-export: satu-satunya salinan private key ada di .pfx.
  - OutDir WAJIB di luar repo. Simpan .pfx di lokasi aman; jangan pernah commit atau kirim lewat chat/email.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\signing\New-CodeSigningCert.ps1 -OutDir D:\secure\networkspeed-signing
#>
param(
    [Parameter(Mandatory = $true)][string]$OutDir,
    [string]$Subject = 'CN=KCT Network Speed Code Signing',
    [ValidateRange(1, 10)][int]$Years = 3
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SigningCommon.ps1')

$OutDir = [System.IO.Path]::GetFullPath($OutDir)
Assert-OutsideRepo -Path $OutDir -What 'OutDir'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$pfxPath = Join-Path $OutDir 'NetworkSpeedCodeSigning.pfx'
$cerPath = Join-Path $OutDir 'NetworkSpeedCodeSigning.cer'
if ((Test-Path $pfxPath) -or (Test-Path $cerPath)) {
    throw "Refusing to overwrite existing files in $OutDir. Move them first."
}

$password = Read-Host -AsSecureString 'Password for the .pfx'
$confirm = Read-Host -AsSecureString 'Repeat password'
if ((ConvertFrom-SecureStringPlain $password) -ne (ConvertFrom-SecureStringPlain $confirm)) {
    throw 'Passwords do not match.'
}
if ((ConvertFrom-SecureStringPlain $password).Length -lt 12) {
    throw 'Use a password of at least 12 characters.'
}

$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Subject `
    -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
    -KeyExportPolicy Exportable -KeyUsage DigitalSignature `
    -NotAfter (Get-Date).AddYears($Years) -CertStoreLocation 'Cert:\CurrentUser\My'
try {
    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $password | Out-Null
    Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null
}
finally {
    Remove-Item -Path ("Cert:\CurrentUser\My\" + $cert.Thumbprint) -DeleteKey -ErrorAction SilentlyContinue
}

Write-Host "Created:"
Write-Host "  Private key : $pfxPath  (keep secret)"
Write-Host "  Public cert : $cerPath  (give to IT)"
Write-Host "  Subject     : $Subject"
Write-Host "  Thumbprint  : $($cert.Thumbprint)"
Write-Host "  Valid until : $($cert.NotAfter.ToString('yyyy-MM-dd'))"
