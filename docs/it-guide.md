# Panduan IT — Network Speed

Panduan ini untuk tim IT yang memasang, meng-upgrade, dan meng-uninstall Network Speed di PC kantor. Semua perintah dijalankan sebagai **administrator**.

## Ringkasan

| Komponen | Lokasi | Keterangan |
|---|---|---|
| Aplikasi (overlay, per user) | `C:\Program Files\NetworkSpeed\app\` | Autostart untuk semua user lewat `HKLM\...\Run`. User bisa mematikannya untuk dirinya di Settings. |
| Windows Service | `C:\Program Files\NetworkSpeed\service\` | `NetworkSpeedService`, akun `NT AUTHORITY\LocalService`, start otomatis, restart otomatis 3× bila crash. |
| Data PC | `%ProgramData%\NetworkSpeed\` | `history.db` dan log service. Hanya SYSTEM, Administrators, dan LocalService yang punya akses. |
| Data per user | `%LOCALAPPDATA%\NetworkSpeed\` | `settings.json`, `state.json`, dan log aplikasi. |
| Pengaturan PC | `HKLM\SOFTWARE\NetworkSpeed` | `UpdateFolder` (lokasi shared folder update). Diisi installer; tampil read-only di Settings. |

- Installer sudah membawa runtime .NET (self-contained), jadi tidak perlu memasang .NET terpisah.
- Target: Windows 10 22H2 dan Windows 11, x64.

## 1. Sertifikat (sekali per PC, sebelum instalasi pertama)

Installer ditandatangani dengan sertifikat **self-signed** kantor. Supaya Windows dan antivirus mempercayainya, impor file `NetworkSpeedCodeSigning.cer` (dari tim pengembang) ke **Local Machine**. Skrip build tidak pernah mengimpor sertifikat ini secara otomatis.

```powershell
Import-Certificate -FilePath .\NetworkSpeedCodeSigning.cer -CertStoreLocation Cert:\LocalMachine\Root
Import-Certificate -FilePath .\NetworkSpeedCodeSigning.cer -CertStoreLocation Cert:\LocalMachine\TrustedPublisher
```

Untuk banyak PC, sertifikat yang sama bisa didistribusikan lewat Group Policy (Computer Configuration → Public Key Policies → *Trusted Root Certification Authorities* dan *Trusted Publishers*).

## 2. Verifikasi sebelum distribusi

Jalankan di folder rilis, sebelum file disalin ke shared folder:

```powershell
Get-AuthenticodeSignature .\NetworkSpeedSetup-1.0.0.exe | Format-List Status, SignerCertificate, TimeStamperCertificate
(Get-FileHash .\NetworkSpeedSetup-1.0.0.exe -Algorithm SHA256).Hash.ToLower()
Get-Content .\latest.json
```

- `SignerCertificate` harus sertifikat kantor (cocokkan thumbprint-nya dengan yang diberikan tim pengembang), dan `TimeStamperCertificate` harus terisi.
- Hash harus sama persis dengan nilai `sha256` di `latest.json`.
- `Status` = `Valid` di PC yang sudah mengimpor sertifikat. Di PC yang belum mengimpornya, status `UnknownError` (root tidak dipercaya) masih wajar.

## 3. Instalasi silent

```powershell
.\NetworkSpeedSetup-1.0.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATEFOLDER="\\server\share\NetworkSpeed" /LOG="C:\Windows\Temp\NetworkSpeedSetup.log"
```

| Parameter | Arti |
|---|---|
| `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART` | Tanpa tampilan, tanpa dialog, tanpa restart otomatis. |
| `/UPDATEFOLDER="<path>"` | Opsional. Shared folder berisi `latest.json` (UNC atau path lokal penuh). Tanpa parameter ini, cek update di aplikasi tidak aktif. Pada upgrade, nilai lama dipertahankan bila parameter tidak diberikan. |
| `/MERGETASKS="!startmenu"` | Opsional. Tidak membuat shortcut Start Menu. |
| `/LOG="<file>"` | Opsional. Log installer untuk diagnosis. |

- Lokasi instalasi tetap `C:\Program Files\NetworkSpeed\` dan tidak bisa diubah.
- Aplikasi mulai berjalan saat user login berikutnya. Instalasi interaktif menawarkan opsi "Launch Network Speed".

## 4. Upgrade

Jalankan installer versi baru dengan parameter yang sama (`/UPDATEFOLDER` boleh dihilangkan). Installer akan:

1. Mengirim sinyal keluar ke aplikasi di **semua sesi** user. Aplikasi menutup diri dengan sopan.
2. Menunggu maksimal 15 detik. Aplikasi yang masih berjalan dihentikan paksa.
3. Menghentikan service, mengganti file, lalu menjalankan service lagi.

Pengaturan user, riwayat, dan `UpdateFolder` tetap ada. Aplikasi berjalan lagi saat user login berikutnya [ASSUMPTION spec §8], atau bisa dijalankan manual dari Start Menu.

**Downgrade ditolak:** installer versi lebih lama keluar dengan exit code ≠ 0 tanpa mengubah apa pun. Instalasi ulang versi yang sama diperbolehkan (repair).

## 5. Uninstall

```powershell
& "C:\Program Files\NetworkSpeed\unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

- Menutup aplikasi, menghapus service, file program, autostart, dan `HKLM\SOFTWARE\NetworkSpeed`.
- **Data disimpan secara default.** Tambahkan `/REMOVEDATA` untuk juga menghapus `%ProgramData%\NetworkSpeed\` (riwayat PC).
- Data per user (`%LOCALAPPDATA%\NetworkSpeed\`) tidak pernah dihapus oleh uninstaller.
- Uninstaller menyalin dirinya ke folder sementara lalu langsung kembali. Bila skrip deployment perlu menunggu, tunggu sampai `unins000.exe` hilang dari folder instalasi.

## 6. Exit code installer

| Kode | Arti |
|---|---|
| 0 | Sukses |
| 1 | Setup gagal dimulai, **termasuk downgrade ditolak** |
| 2 | Dibatalkan sebelum instalasi |
| 3 | Error fatal saat persiapan |
| 4 | Error fatal saat instalasi |
| 5 | Dibatalkan saat instalasi |
| 6 | Proses setup dihentikan paksa |
| 7 | Persiapan instalasi gagal (tahap *Preparing to Install*) |
| 8 | Persiapan instalasi gagal dan PC perlu restart |

Kode mengikuti Inno Setup 6.7. Terverifikasi pada uji penerimaan 2026-11-12: install, upgrade, dan reinstall = 0; downgrade = 1. Upgrade silent tidak meminta restart.

## 7. Shared folder update

```
\\server\share\NetworkSpeed\
  latest.json
  NetworkSpeedSetup-1.0.1.exe
```

- `latest.json` dibuat otomatis oleh `tools\build-release.ps1`: `version`, `installer` (hanya nama file), `sha256`, dan `notes`. Ukuran maksimal 16 KB.
- Aplikasi membaca `latest.json` 30 detik setelah start, lalu tiap 24 jam. Bila ada versi baru, user mendapat satu notifikasi *"A new version (x.y.z) is available. Your IT team will install it."* (sekali per versi), dan statusnya terlihat di Settings → Updates.
- Aplikasi **tidak** memasang update sendiri; instalasi tetap dilakukan IT.
- Folder tidak terjangkau (di luar kantor, VPN mati) ditangani diam-diam; aplikasi mencoba lagi 24 jam kemudian.
- **Rekomendasi:** hak tulis shared folder hanya untuk admin rilis; user cukup hak baca.

## 8. Keamanan sertifikat

- **Private key (`.pfx`) tidak pernah disimpan di repo atau dikirim lewat chat/email.** Siapa pun yang memegangnya bisa membuat program yang dipercaya oleh semua PC kantor. Simpan di lokasi aman dengan password kuat; skrip build menolak `.pfx` yang berada di dalam folder repo.
- **Bila private key bocor:**
  1. Hapus sertifikat lama dari *Trusted Root* dan *Trusted Publishers* di semua PC.
  2. Buat sertifikat baru (`tools\signing\New-CodeSigningCert.ps1`).
  3. Bangun ulang rilis dengan sertifikat baru.
  4. Distribusikan `.cer` yang baru.
- **Rotasi sebelum kedaluwarsa (3 tahun):** buat sertifikat baru, distribusikan `.cer`-nya ke semua PC, lalu mulai tanda tangani rilis dengan sertifikat baru. Signature lama tetap valid berkat timestamp.
- Untuk rilis publik, ganti dengan sertifikat publik (OV/EV) atau Azure Trusted Signing.

## 9. Membuat rilis (tim pengembang)

```powershell
# Sekali: buat sertifikat (folder di luar repo).
powershell -ExecutionPolicy Bypass -File tools\signing\New-CodeSigningCert.ps1 -OutDir D:\secure\networkspeed-signing

# Setiap rilis: test, publish self-contained, tanda tangan, installer, latest.json -> artifacts\release\
powershell -ExecutionPolicy Bypass -File tools\build-release.ps1 -Version 1.0.1 -PfxPath D:\secure\networkspeed-signing\NetworkSpeedCodeSigning.pfx -Notes "Bug fixes"
```

- Butuh .NET 10 SDK dan Inno Setup 6.
- Timestamp memakai `http://timestamp.digicert.com`; ini satu-satunya panggilan internet saat build.
