# Network Speed

Aplikasi Windows yang menampilkan kecepatan upload/download jaringan secara real-time sebagai overlay kecil di taskbar (kiri, tengah, atau kanan).

- Desain: [`docs/superpowers/specs/2026-11-11-network-speed-design.md`](docs/superpowers/specs/2026-11-11-network-speed-design.md)
- Status: aplikasi overlay (Plan 2), Windows Service (Plan 3), dan integrasinya — riwayat, kuota, per aplikasi (Plan 4) — sudah ada. Installer silent, code signing self-signed, dan cek update dari shared folder (Plan 5) juga sudah ada; panduan untuk IT ada di [docs/it-guide.md](docs/it-guide.md).

## Kebutuhan

- Windows 10 22H2 atau Windows 11 (x64)
- .NET 10 SDK untuk build (`winget install Microsoft.DotNet.SDK.10`)
- Tidak butuh hak admin untuk menjalankan aplikasi

## Build & jalankan

Perintah dijalankan dari root repo.

```
dotnet restore
dotnet build -c Release -warnaserror
dotnet test
dotnet format --verify-no-changes
dotnet run --project src/NetworkSpeed.App
```

Build mandiri (self-contained), mis. untuk PC uji tanpa .NET:

```
dotnet publish src/NetworkSpeed.App -c Release -r win-x64 --self-contained -o out/app
```

Alat diagnostik untuk mencetak kecepatan per detik ke konsol, untuk dibandingkan dengan Task Manager:

```
dotnet run --project tools/SpeedProbe -- 60
```

## Rilis & instalasi

- Membuat installer: `powershell -ExecutionPolicy Bypass -File tools\build-release.ps1 -Version x.y.z -PfxPath <pfx di luar repo>` → `artifacts\release\NetworkSpeedSetup-x.y.z.exe` + `latest.json`. Butuh Inno Setup 6.
- Sertifikat code signing dibuat sekali dengan `tools\signing\New-CodeSigningCert.ps1` (private key tidak pernah di repo).
- Instalasi, upgrade, uninstall silent, exit code, dan verifikasi: [docs/it-guide.md](docs/it-guide.md).
- Settings → Updates menampilkan folder update (diatur IT, read-only) dan status versi baru; App hanya memberi tahu, tidak memasang.

## Pemakaian

- **Ikon tray:** Settings…, Show/Hide overlay, History…, Per-app usage…, Exit. Klik kanan overlay membuka menu yang sama.
- **History:** grafik Live (60 detik, dari aplikasi), 24 jam (per menit) dan 7 hari (per jam) dari service.
- **Per-app usage:** pemakaian per aplikasi milik sesi Anda sejak service mulai atau hari ini; aplikasi lain dan layanan Windows digabung ke baris "System". Angkanya perkiraan (diskalakan ke total adapter).
- **Data limit (quota):** batas harian/bulanan (GB, 1 GB = 1024³ byte) di Settings; notifikasi pada 80% dan 100%, sekali per periode. Ringkasan tampil di tooltip overlay.
- Riwayat, per aplikasi, dan kuota butuh service `NetworkSpeedService`; tanpa service tampil "not available" dan overlay tetap berjalan.
- **Hotkey** Show/Hide: default `Ctrl+Alt+N`, bisa diubah di Settings.
- **Tooltip overlay:** adapter, SSID, IPv4 lokal, total sesi, dan latency.
- Menjalankan aplikasi untuk kedua kalinya membuka jendela Settings milik instance yang sedang berjalan.

## Lokasi data (per user)

| Isi | Lokasi |
|---|---|
| Pengaturan | `%LOCALAPPDATA%\NetworkSpeed\settings.json` |
| Log (5 × 1 MB, berotasi) | `%LOCALAPPDATA%\NetworkSpeed\logs\`: `app.log` (proses utama), `viewer-history.log` / `viewer-perapp.log` (jendela riwayat / per aplikasi) |

- `settings.json` boleh diedit manual. Field yang tidak valid dikembalikan ke default satu per satu, dan field yang tidak dikenal diabaikan.
- File yang rusak di-rename ke `settings.json.corrupt-<waktu>` (maksimal 3 disimpan).
- Log **tidak** memuat IP, SSID, host ping, nama proses, atau ID aplikasi taskbar.

## Dukungan posisi (matriks F-07c)

| Taskbar | Left | Center | Right |
|---|---|---|---|
| Windows 11, rata tengah | ✅ | ✅ | ✅ |
| Windows 11, rata kiri | ❌ (pojok kiri dipakai Start) | ⏳ uji penerimaan | ⏳ uji penerimaan |
| Windows 10, bawah/atas | ❌ | ⏳ uji penerimaan | ⏳ uji penerimaan |
| Taskbar vertikal (Windows 10) | ❌ | ❌ | ❌ (overlay disembunyikan) |

- Posisi yang tidak didukung tampil nonaktif di Settings, beserta alasannya.
- Kalau posisi pilihan tidak muat, overlay pindah ke Right. Kalau tetap tidak muat, overlay disembunyikan agar tidak menutupi tombol aplikasi.

## Batasan yang diketahui

- **SSID kosong di tooltip:** di Windows 11 24H2+, SSID hanya terbaca bila izin lokasi diberikan.
- **Taskbar vertikal** belum didukung.
- **Saat aplikasi baru mulai,** overlay di posisi Center/Left bisa sesaat tampil di Right (≤ 1 detik), sampai UI Automation taskbar siap.
- **Penggunaan memori:** proses utama (overlay + tray) saat idle rata-rata ±78–85 MB private dan sesekali mencapai ±88 MB (target < 80 MB belum selalu tercapai; fluktuasi berasal dari heap GC di antara koleksi). Jendela History dan Per-app usage berjalan di **proses terpisah** (`NetworkSpeed.App.exe --viewer …`, ±88–92 MB) yang keluar saat jendelanya ditutup atau saat aplikasi utama keluar, sehingga tidak menambah memori proses utama.

## Struktur

| Folder | Isi |
|---|---|
| `src/NetworkSpeed.Core` | Logika murni (kecepatan, format, adapter, posisi, pengaturan, log) yang diuji unit |
| `src/NetworkSpeed.Interop` | Akses Windows: counter `GetIfTable2`, layout taskbar (UIA/Win32), shell state, SSID, ping |
| `src/NetworkSpeed.App` | Aplikasi WPF: overlay, tray, Settings |
| `tests/` | xUnit v3 (runner Microsoft.Testing.Platform) |
| `tools/SpeedProbe` | Alat diagnostik konsol |
| `spikes/` | Prototipe sekali pakai (bukan kode produksi) |
