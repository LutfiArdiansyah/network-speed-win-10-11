# Desain — Network Speed Monitor di Taskbar Windows

- Tanggal: 2026-11-11
- Status: **disetujui pemilik produk (2026-11-11)**
- Sumber: hasil brainstorm di `docs/brainstorm-network-speed.md` (kode poin seperti `F-07` merujuk ke dokumen itu). **Jika berbeda, dokumen ini yang berlaku.**
- Penanda: **[ASSUMPTION]** = keputusan/tebakan perancang yang belum dikonfirmasi pemilik produk; **[BUILDER]** = diserahkan ke builder.

---

## 1. Tujuan & alasan

Aplikasi desktop Windows yang menampilkan kecepatan upload/download jaringan secara real-time **di taskbar**. Pengguna bisa memilih posisi kiri, tengah (setelah app yang di-pin), atau kanan (sebelum system tray).

Dipakai **internal kantor dulu** (distribusi manual), dengan kemungkinan **rilis publik** nanti. Karena itu kualitas kode, keamanan, dan privasi dibuat setara produk publik sejak awal. Yang disederhanakan hanya distribusi dan signing.

### Ukuran sukses
- Angka kecepatan akurat (selisih < 5% dibanding Task Manager pada adapter yang sama).
- Overlay tidak pernah menutupi ikon taskbar lain, tidak berkedip, dan tidak "hilang" setelah Explorer restart, sleep/resume, atau perubahan monitor.
- Ringan: UI CPU < 1% saat idle, RAM UI < 80 MB (private memory; dinaikkan dari 60 MB oleh pemilik produk 2026-11-12). Terukur: proses utama idle rata-rata ±78–85 MB (puncak ±88 MB); diterima sementara oleh pemilik produk 2026-11-12, investigasi menyusul; service CPU < 1%, RAM < 50 MB [ASSUMPTION]; `history.db` < 5 MB dalam kondisi normal.
- Riwayat & kuota mencakup seluruh waktu PC menyala, bukan hanya saat App terbuka.
- Tidak crash. Semua kegagalan deteksi ditangani dengan fallback, bukan error ke pengguna.

### Non-goals
- Speed test bawaan (X-05), export CSV (H-03), telemetri (P-08), multi-bahasa (hanya Inggris), pengaturan terpusat via GPO, MSIX/winget/Store (fase publik nanti).

### Anti-goals (gagal walau "jalan")
- Inject ke `explorer.exe` atau memodifikasi taskbar Windows secara langsung.
- Overlay menimpa ikon lain atau berada di posisi yang salah tanpa fallback.
- Menampilkan lonjakan kecepatan palsu (setelah sleep, counter reset, dsb.).
- Database atau log yang terus membesar tanpa batas.
- Service yang membuka celah: user biasa bisa memerintah service, atau user A bisa melihat data aplikasi user B.

---

## 2. Batasan

| # | Batasan |
|---|---------|
| C-01 | OS: **Windows 10 22H2** (keputusan pemilik produk, termasuk PC yang masa dukungan Microsoft-nya sudah habis atau memakai ESU) **dan Windows 11 23H2+** [ASSUMPTION: batas versi minimum]. Arsitektur: **x64 saja** [ASSUMPTION, keputusan perancang]; ARM64 ditambahkan saat rilis publik. |
| C-02 | Teknologi: **C# + .NET 10 LTS**. UI: **WPF**. Service: **.NET Worker Service** sebagai Windows Service. |
| C-03 | Integrasi taskbar: **jendela overlay** (Opsi A) + **UI Automation**. Tidak ada injection, tidak ada DeskBand. Satu jalur kode untuk Windows 10 dan 11. |
| C-04 | Distribusi: **manual oleh IT** (keputusan pemilik produk), satu **installer `.exe` per-machine** yang mendukung mode **silent** (butuh admin). Pengguna diasumsikan **bukan admin** [ASSUMPTION, dasar: install & update dijalankan IT]. Tidak ada versi portable. |
| C-05 | Signing: **sertifikat self-signed** (lihat §9). |
| C-06 | Update: cek versi baru dari **shared folder kantor** (lihat §10). |
| C-07 | Bahasa UI: **Inggris**. |
| C-08 | Antivirus di kantor: **ESET**. Tidak ada uji formal (P-10 dibatalkan), tetapi desain menghindari pola yang biasa ditandai AV (injection, hook global, unpacking runtime). |
| C-09 | Tidak ada data yang dikirim keluar PC, kecuali ping (X-01) ke host yang dipilih pengguna dan pembacaan shared folder update. |

---

## 3. Arsitektur

```
┌────────────────────────────── PC pengguna ──────────────────────────────┐
│                                                                          │
│  NetworkSpeed.App (WPF, per user, tanpa admin, autostart saat login)     │
│   ├─ Overlay window (di atas taskbar, satu per monitor yang dipilih)     │
│   ├─ Tray icon + menu, jendela Settings                                  │
│   ├─ Jendela riwayat & per aplikasi → proses viewer terpisah (exe sama,  │
│   │   --viewer), keluar saat jendela ditutup / App utama keluar         │
│   ├─ Live speed sampler (GetIfTable2) — hanya untuk overlay              │
│   ├─ Taskbar locator (UI Automation + event Windows)                     │
│   ├─ Quota notifier (ambang per user, data dari service)                 │
│   └─ Pipe client ──────────────┐                                         │
│                                │ named pipe (read-only, ACL)             │
│  NetworkSpeed.Service (Windows Service, start otomatis sejak boot)       │
│   ├─ History sampler (GetIfTable2) → agregat per menit/hari              │
│   ├─ History store (SQLite, per PC, %ProgramData%)                       │
│   ├─ ETW collector (trafik per proses)                                   │
│   └─ Pipe server (riwayat per PC + data per aplikasi per sesi)           │
└──────────────────────────────────────────────────────────────────────────┘
```

- **App** berjalan sebagai user yang login, satu instance per sesi (mutex bernama per sesi).
- **Service** adalah sumber tunggal **riwayat & kuota per PC** (keputusan pemilik produk: opsi 1b) dan **data per aplikasi**. Karena berjalan sejak boot, trafik sebelum login, setelah logout, dan saat App ditutup tetap tercatat, dan trafik tidak terhitung ganda di PC multi-user.
- **App tanpa service:** overlay kecepatan live tetap berfungsi penuh. Riwayat, kuota, dan per aplikasi tampil "Not available".
- App **tidak pernah membuka database secara langsung**; semua akses lewat pipe.
- **Proses viewer** (keputusan pemilik produk 2026-11-12, demi target memori §1): jendela riwayat dan per aplikasi dijalankan oleh exe yang sama dengan argumen `--viewer`, tanpa mutex single instance, overlay, tray, atau kuota. Viewer membaca `settings.json` tanpa menulis, memakai pipe service yang sama (cek sesi 0), menulis log ke file sendiri, dan keluar saat jendelanya ditutup atau App utama keluar. Data Live 60 detik diteruskan lewat argumen.
- Struktur solusi (proyek, nama namespace) **[BUILDER]**, dengan syarat logika bisnis (perhitungan kecepatan, agregasi, aturan posisi) terpisah dari detail Win32/UI agar bisa diuji unit.

---

## 4. Fitur

### 4.1 Pengukuran kecepatan
| # | Perilaku |
|---|----------|
| F-01 | Baca counter byte in/out per adapter dari `GetIfTable2` (64-bit). Kecepatan = delta byte / delta waktu, dengan waktu dari **monotonic clock** (`Stopwatch`), bukan jam dinding (E-14). |
| F-02 | Satuan: **bit/s** atau **byte/s**, skala otomatis (K/M/G). Desimal: [ASSUMPTION] skala 1000 untuk bit/s dan 1024 untuk byte/s, mengikuti Task Manager. Format angka **[BUILDER]**, asal lebarnya stabil supaya teks tidak "loncat". |
| F-03 | Interval refresh 0,5–5 detik, default 1 detik. |
| F-04 | Pilihan adapter: **Auto** (default: adapter yang Up dan punya default gateway), **All** (jumlah semua adapter fisik + VPN yang tidak dikecualikan), atau adapter tertentu (disimpan berdasarkan `InterfaceGuid`, bukan nama). |
| F-05 | Adapter yang dikecualikan secara default: loopback, Hyper-V/WSL virtual switch, adapter filter/miniport tanpa trafik nyata. **VPN**: [ASSUMPTION] dalam mode All, adapter VPN **dikecualikan** secara default karena trafiknya sudah terhitung di adapter fisik; bisa diaktifkan manual. |
| F-06 | Tooltip: nama adapter, SSID (kalau Wi-Fi), IPv4 lokal, total ↑/↓ sesi ini, latency (kalau X-01 aktif). SSID hanya dibaca saat tooltip dibuka: sejak Windows 11 24H2 membaca SSID dihitung akses lokasi (icon lokasi muncul); bila izin lokasi ditolak, baris SSID tidak tampil. |
| F-08 | Menu tray & klik kanan overlay: Settings, Show/Hide, History, Per-app usage, Check for updates, Exit. |

### 4.2 Posisi overlay (F-07)
| # | Perilaku |
|---|----------|
| F-07 | Posisi: **Left** (setelah tombol Start/Widgets yang ada di kiri), **Center** (tepat setelah tombol app terakhir di taskbar), **Right** (tepat di kiri system tray/notification area). |
| F-07a | Setiap posisi punya status didukung/tidak didukung per kombinasi: OS (10/11) × perataan taskbar (kiri/tengah) × orientasi taskbar (bawah/atas/samping, Windows 10). Posisi yang tidak didukung tampil **nonaktif** di Settings dengan alasan singkat. **Matriks setelah spike (go/no-go 2026-11-12)**, lihat tabel di bawah. Kombinasi ⏳ menjadi uji penerimaan Plan 2 (`docs/superpowers/spikes/2026-11-12-taskbar-locator-results.md` §6). |
| F-07c | Matriks dukungan: **Win11 rata tengah** → Left ✅, Center ✅, Right ✅ (teruji). **Win11 rata kiri** → Left ❌ (pojok kiri ditempati Start/app), Center ⏳, Right ⏳. **Win10 bawah/atas** → Left ❌, Center ⏳, Right ⏳. **Taskbar vertikal (Win10)** → semua ❌ untuk versi pertama; overlay disembunyikan dan Settings menampilkan alasannya. |
| F-07d | Sumber data posisi (hasil spike): Win11 memakai anak `AutomationId="TaskbarFrame"` (tombol app `ClassName="Taskbar.TaskListButtonAutomationPeer"`, `StartButton`); Win10 memakai `ReBarWindow32 > MSTaskSwWClass > MSTaskListWClass`; tepi kiri tray dari Win32 `TrayNotifyWnd`. `AutomationId` tombol app memuat AppUserModelID/path, jadi **tidak boleh dicatat ke log**. Taskbar monitor kedua: `Shell_SecondaryTrayWnd`. |
| F-07b | Kalau deteksi gagal saat runtime (elemen UI Automation tidak ditemukan, ruang tidak cukup, struktur taskbar berubah), overlay **otomatis pindah ke Right**. Kalau Right juga gagal, overlay diletakkan berdasarkan koordinat `Shell_TrayWnd` / `TrayNotifyWnd` (Win32) tanpa UI Automation. Kejadian ini dicatat ke log (level warn), tanpa dialog error. |
| T-06 | Offset manual X/Y (piksel, ±200) per posisi sebagai koreksi. |
| T-05 | Posisi dihitung ulang saat: pin/unpin/app buka/tutup (event UI Automation pada daftar tombol taskbar), perubahan ikon tray, `TaskbarCreated` (Explorer restart), `WM_SETTINGCHANGE` (perataan taskbar, tema), `WM_DISPLAYCHANGE` / `WM_DPICHANGED`, resume dari sleep. Event di-*debounce* (mis. 250 ms) supaya tidak menghitung ulang berkali-kali; ditambah polling ringan sebagai jaring pengaman [BUILDER: interval, mis. 2–5 detik]. **Hasil spike:** scan UIA penuh ±100–200 ms dan polling UIA tiap 2 detik ≈ 0,7% CPU total, di atas target. Karena itu polling **hanya memakai Win32** (`GetWindowRect` `Shell_TrayWnd`/`TrayNotifyWnd`, murah); scan UIA hanya dijalankan saat ada event atau saat rect Win32 berubah, dan query UIA dibatasi ke `Children` milik `TaskbarFrame`. Scan UIA selalu di thread background. |
| T-09 | Overlay: jendela tanpa border, transparan, `WS_EX_TOOLWINDOW` (tidak muncul di Alt+Tab/taskbar), `WS_EX_NOACTIVATE` (tidak mencuri fokus), topmost. Saat taskbar di-klik, overlay harus tetap di atasnya (re-assert z-order saat taskbar aktif). |
| T-10 | Ukuran overlay menyesuaikan teks; di posisi Center/Left, overlay tidak boleh menutupi tombol app. Kalau ruang tidak cukup → F-07b. |

### 4.3 Tampilan
| # | Perilaku |
|---|----------|
| D-01 | Ikuti tema terang/gelap Windows secara otomatis (bisa di-override). |
| D-02 | Font, ukuran, warna teks (terpisah untuk ↑ dan ↓), transparansi latar. |
| D-03 | Layout 1 baris (`↑ 1.2 MB/s ↓ 5.4 MB/s`) atau 2 baris. |
| D-04 | Warna ambang: kalau kecepatan > nilai yang diatur pengguna, teks berganti warna. |
| D-05 | Sembunyi otomatis saat ada aplikasi fullscreen di monitor yang sama (`SHQueryUserNotificationState` / deteksi jendela foreground fullscreen). |
| D-06 | Mode kontras tinggi: pakai warna sistem. Overlay & Settings punya nama/nilai yang terbaca oleh screen reader (UI Automation properties). |
| E-01 | Taskbar auto-hide: overlay ikut sembunyi/muncul mengikuti posisi taskbar. |
| E-03 | Multi-monitor: pilihan **Primary only** (default), **All taskbars**, atau monitor tertentu. |
| E-10 | Tidak ada adapter aktif: tampilkan "Offline". |

### 4.4 Riwayat & kuota
| # | Perilaku |
|---|----------|
| H-02 | Popup grafik ↑/↓: live (60 detik terakhir dari memori App), 24 jam dan 7 hari (dari service). |
| H-05 | **Per PC**, dikelola service: SQLite di `%ProgramData%\NetworkSpeed\history.db`. ACL folder: tulis hanya untuk akun service, SYSTEM, dan Administrators; user biasa **tidak** punya akses langsung. Service mencatat **setiap adapter kecuali loopback secara terpisah**, berdasarkan `InterfaceGuid` (diperbarui 2026-11-12). Pengecualian F-05 diterapkan saat memilih adapter yang dijumlahkan, bukan saat mencatat, karena mode Auto bisa memilih adapter virtual (Hyper-V external switch). Tabel: agregat **per menit** (retensi 7 hari) dan **total per hari** (retensi 62 hari). Skema detail **[BUILDER]**. |
| H-06 | Service menyampel tiap 1 detik ke ring buffer memori, menulis ke disk tiap 1 menit dalam satu transaksi. Saat service berhenti (shutdown), data menit berjalan di-*flush*. Pembersihan data kedaluwarsa sekali sehari dan saat start. Mode WAL. |
| H-07 | App meminta riwayat lewat pipe dengan parameter rentang waktu + daftar adapter; service menjumlahkan sesuai pilihan adapter user (F-04). Rentang dan jumlah titik dibatasi (mis. maks 10.080 titik per permintaan). Riwayat adapter bersifat per PC dan **boleh dilihat semua user di PC itu**, karena tidak memuat nama aplikasi atau data pribadi [keputusan perancang]. |
| H-01 | Kuota: total pemakaian **per PC** dari service. **Ambang dan notifikasi adalah preferensi per user** di `settings.json` (batas harian dan/atau bulanan dalam GB, tanggal mulai periode, default tanggal 1), sehingga pipe tetap read-only [keputusan perancang]. Notifikasi toast di 80% dan 100% [ASSUMPTION], masing-masing sekali per periode per user. Kuota dihitung dari adapter pilihan F-04 user tersebut [ASSUMPTION]. |
| E-05/E-07 | Di App dan service: setelah resume dari sleep atau saat counter adapter turun (driver restart), sampel pertama dibuang (baseline baru); delta negatif = 0. |
| E-11 | `history.db` rusak (oleh service) atau `settings.json` rusak (oleh App) → rename ke `.corrupt-<timestamp>`, buat baru dengan default, catat di log. Maksimal 3 file `.corrupt` disimpan. |
| E-15 | Penyimpanan data per hari memakai **tanggal lokal PC**. Perubahan zona waktu atau DST tidak menghapus data; menit yang "berulang" saat jam mundur dijumlahkan ke baris yang sama [keputusan perancang]. |

### 4.5 Fitur tambahan
| # | Perilaku |
|---|----------|
| X-01 | Ping ICMP ke host yang bisa dikonfigurasi (default: default gateway), interval sama dengan refresh atau lebih lambat [BUILDER], timeout 1 detik. Ditampilkan di tooltip dan (opsional) di overlay. Gagal → "timeout", bukan error. |
| X-02 | Toast notifikasi saat koneksi putus dan tersambung kembali (bisa dimatikan). Debounce supaya Wi-Fi yang naik-turun tidak membanjiri notifikasi. |
| X-03 | Hotkey global Show/Hide, bisa diubah; default [ASSUMPTION] `Ctrl+Alt+N`. Kalau bentrok (registrasi gagal), beri tahu di Settings, jangan crash. |

### 4.6 Pemakaian per aplikasi (H-04) — Windows Service
| # | Perilaku |
|---|----------|
| S-01 | Service mengumpulkan byte ↑/↓ per proses lewat ETW real-time, provider **`Microsoft-Windows-Kernel-Network`** (GUID `7dd42a49-5329-4832-8dfd-43d979153a88`), keyword 0x30 (IPv4+IPv6), level Informational (keputusan go/no-go 2026-11-12). Event yang dihitung: TCP *Data sent* / *Data received* dan UDP *Data sent/received over UDP protocol* (IPv4 & IPv6), dengan payload `PID` dan `size`. Event dikenali dari **ID event** (dari manifest, `wevtutil gp Microsoft-Windows-Kernel-Network /ge /gm`, 2026-11-12), bukan dari nama: spike membuktikan pencocokan nama itu rapuh. **Kirim:** 10 (TCPv4), 26 (TCPv6), 42 (UDPv4), 58 (UDPv6). **Terima:** 11, 27, 43, 59. Event 18/34 ("bytes copied in protocol on behalf of user") **tidak dihitung**, karena itu salinan data yang sama ke buffer aplikasi; 14/30 (retransmit) juga tidak dihitung. Trafik loopback diabaikan. |
| S-02 | Data disimpan **di memori** service: ringkasan per proses untuk sesi berjalan dan hari ini. [ASSUMPTION] Tidak disimpan ke disk oleh service; riwayat per aplikasi tidak termasuk H-05. |
| S-03 | Akun service: **`NT AUTHORITY\LocalService` bawaan**, tanpa grup *Performance Log Users* dan tanpa izin ETW tambahan. Spike membuktikan security default Windows mengizinkan LocalService mengaktifkan provider Kernel-Network (keputusan go/no-go 2026-11-12). Installer **tidak** mengubah grup atau security ETW. `LocalSystem` tidak dipakai. |
| S-04 | Named pipe dengan ACL: hanya *Interactive Users* yang bisa connect; protokol **read-only** (hanya query: status service, daftar adapter, riwayat H-07, total kuota, per aplikasi), pesan berformat JSON dengan ukuran maksimum (mis. 64 KB untuk request; response riwayat dibatasi lewat H-07) dan versi protokol. Batas jumlah koneksi bersamaan (mis. 16) dan timeout per request supaya satu klien tidak bisa membuat service macet. |
| S-05 | Service memakai identitas pemanggil (`GetNamedPipeClientSessionId`) dan hanya mengembalikan proses dari **sesi yang sama**. Trafik proses sesi 0 (layanan Windows, Windows Update, antivirus) ditampilkan sebagai **satu baris agregat "System"** tanpa rincian [ASSUMPTION, keputusan perancang]. Session ID per PID diambil dari snapshot proses sistem (`NtQuerySystemInformation` / `Process.GetProcesses()`), **bukan** `ProcessIdToSessionId`, karena yang terakhir butuh handle proses dan gagal untuk LocalService (hasil spike). PID yang sudah keluar dan tidak diketahui sesinya dimasukkan ke baris "System" [keputusan perancang: fail closed, tidak pernah ditampilkan ke sesi user mana pun]. |
| S-09 | ETW mencatat payload; counter adapter mencatat seluruh frame (hasil spike: upload ±87–96%, download ±61–75% pada trafik ringan). Per aplikasi ditampilkan sebagai **proporsi**: `bytes_app = etw_app / etw_total × total_adapter` pada jendela waktu yang sama, sehingga jumlahnya sama dengan angka overlay, dan diberi label "approximate". Akurasi unduhan besar diverifikasi di Plan 3. |
| S-06 | App tanpa service → riwayat, kuota, dan per aplikasi "Not available"; overlay live normal. Ketidakcocokan versi protokol → sama. App mencoba connect ulang secara berkala (backoff). |
| S-07 | Recovery: restart otomatis saat crash (1 menit, 3 kali). |
| S-08 | Validasi semua input pipe; error tidak memuat detail internal. |

---

## 5. Pengaturan
- Disimpan per user di `%LOCALAPPDATA%\NetworkSpeed\settings.json`, dengan field `schemaVersion` untuk migrasi ke depan (backward compatibility).
- Nilai yang tidak valid (di luar rentang) dikembalikan ke default per field, bukan seluruh file.
- Pengaturan **tingkat PC** (path shared folder update §10) disimpan di `HKLM\SOFTWARE\NetworkSpeed`, diisi oleh installer, hanya bisa diubah admin. Settings App menampilkannya **read-only**.

## 6. Autostart & instance
- App: autostart untuk semua user lewat `HKLM\...\Run` yang dipasang installer. User yang mematikan autostart di Settings: App membaca preferensi itu saat dijalankan dari autostart (argumen `--autostart`) lalu langsung keluar [keputusan perancang, karena user biasa tidak bisa mengubah HKLM]. Satu instance per sesi user; menjalankan ulang → membuka Settings instance yang sudah ada.
- Service: start otomatis (bukan delayed, supaya trafik awal sejak boot tercatat) [keputusan perancang].
- E-13 Remote Desktop/multi-user: setiap sesi punya instance App sendiri; overlay hanya di sesi tersebut.

## 7. Logging
- File log per user di `%LOCALAPPDATA%\NetworkSpeed\logs\`, rotasi (mis. 5 file × 1 MB). Service log ke Windows Event Log atau file di `%ProgramData%` dengan rotasi yang sama.
- **Tidak mencatat** IP, SSID, nama host ping, atau nama proses pengguna lain. Kesalahan mencatat konteks teknis saja.
- Library logging **[BUILDER]**.

## 8. Installer
- Satu `.exe` per-machine, butuh admin, install ke `C:\Program Files\NetworkSpeed\`. Memasang App + Service, mendaftarkan service dengan akun §4.6 S-03, membuat `%ProgramData%\NetworkSpeed\` dengan ACL H-05, mendaftarkan autostart App untuk semua user (`HKLM\...\Run`, user tetap bisa mematikannya untuk dirinya di Settings), dan membuat shortcut Start Menu.
- **Mode silent** untuk IT, dengan parameter command line: path shared folder update (opsional; kosong = cek update mati), dan opsi tidak membuat shortcut. Exit code yang jelas (sukses / butuh reboot / gagal). Nama parameter mengikuti tool installer **[BUILDER]**, didokumentasikan di README.
- **Upgrade di tempat** (versi lebih baru di atas versi lama, juga lewat silent): menghentikan service, menutup App di semua sesi dengan sopan (sinyal keluar lewat named event, bukan kill), mengganti file, menjalankan ulang service. App di sesi yang aktif dijalankan ulang saat login berikutnya [ASSUMPTION]. Pengaturan & riwayat tetap. Downgrade ditolak.
- **Uninstall** (juga silent): menghentikan dan menghapus service, menghapus file program. Data **disimpan secara default** (`%ProgramData%\NetworkSpeed\` dan `%LOCALAPPDATA%\NetworkSpeed\` setiap user); parameter opsional untuk menghapus data PC [keputusan perancang, karena uninstall silent tidak bisa bertanya].
- Runtime .NET: [ASSUMPTION] **self-contained** (tidak perlu memasang .NET terpisah di setiap PC, cocok untuk distribusi manual). Tool installer: **Inno Setup 6** (keputusan builder, Plan 5); App dan Service dipublish ke subfolder `app\` dan `service\`. Penutupan App: event `Global\NetworkSpeed.AppExit` dibuat service (ACL: hanya Admin/SYSTEM/LocalService boleh men-set), fallback `taskkill` setelah 15 detik. Parameter & exit code: `docs/it-guide.md`.

## 9. Code signing (self-signed)
- Disediakan skrip PowerShell untuk: (1) membuat sertifikat self-signed **khusus code signing** (EKU Code Signing saja, validitas [ASSUMPTION] 3 tahun), (2) menandatangani semua `.exe`/`.dll` milik proyek dan installer, (3) export sertifikat publik (`.cer`) untuk dibagikan.
- **Private key tidak pernah masuk repo.** `.pfx` disimpan di lokasi aman di luar repo, password lewat prompt/environment variable. `.gitignore` memblokir `*.pfx`.
- Signature diberi **timestamp** (RFC 3161) agar tetap valid setelah sertifikat kedaluwarsa. [ASSUMPTION] Memakai timestamp server publik; ini satu-satunya panggilan keluar saat build.
- Agar dipercaya, `.cer` harus **diimpor manual oleh admin/IT** di setiap PC ke store *Trusted Root Certification Authorities* dan *Trusted Publishers* (Local Machine). Dokumentasi langkahnya disertakan. Skrip **tidak** mengimpor otomatis.
- Risiko dicatat di README: kalau private key bocor, siapa pun bisa membuat program yang dipercaya PC kantor. Untuk rilis publik, ganti ke sertifikat publik (OV/EV) atau Azure Trusted Signing.

## 10. Update via shared folder
- Shared folder (UNC, mis. `\\server\share\NetworkSpeed\`) berisi `latest.json` (versi, nama file installer, SHA-256, catatan rilis singkat) dan file installer.
- App mengecek saat start dan tiap 24 jam (bisa dimatikan). Folder tidak bisa diakses (di luar kantor, VPN mati) → diam, coba lagi nanti; tidak ada error ke pengguna.
- Update **dijalankan oleh IT** (keputusan pemilik produk). Cek update di App hanya bersifat **informasi**: versi lebih baru → satu toast "A new version (x.y.z) is available. Your IT team will install it." dan status di Settings; tidak ada tombol install [keputusan perancang]. Toast tidak diulang untuk versi yang sama.
- IT menjalankan installer dari shared folder secara silent. README menyertakan langkah verifikasi untuk IT (cek SHA-256 dan signature sebelum distribusi).
- [Opsional, hanya jika user lokal adalah admin] Tombol "Install now" muncul; App memverifikasi **SHA-256** dan **signature Authenticode** sebelum menjalankan installer. Gagal verifikasi → tolak, catat di log.
- Verifikasi signature **tidak bergantung** pada `.cer` sudah diimpor di PC: App memeriksa integritas signature dan mencocokkan sertifikat penanda tangan dengan **daftar thumbprint yang di-pin** di App [keputusan perancang]. Rotasi sertifikat: thumbprint sertifikat baru dimasukkan ke daftar pin lewat satu rilis yang masih ditandatangani sertifikat lama, minimal beberapa bulan sebelum sertifikat lama kedaluwarsa.
- `latest.json` divalidasi (ukuran maksimum, format versi, nama file tanpa path traversal).
- Hak tulis shared folder sebaiknya hanya untuk admin rilis (dicatat sebagai rekomendasi operasional, bukan bagian kode).

## 11. Prototipe & validasi (sebelum fitur lengkap)
Risiko terbesar adalah F-07 dan S-03. Urutan kerja yang disarankan:
1. **Spike posisi taskbar** di Windows 10 dan 11 (perataan kiri/tengah, 100%/150%/200% DPI, 2 monitor, auto-hide, Explorer restart). Hasilnya: matriks dukungan F-07a.
2. **Spike ETW** dengan akun `LocalService` untuk menentukan S-01/S-03.
3. Baru setelah itu fitur lengkap.

**Status: GO, disetujui pemilik produk 2026-11-12.** Hasil: `docs/superpowers/spikes/2026-11-12-taskbar-locator-results.md` dan `2026-11-12-etw-results.md`. Kombinasi posisi yang belum diuji menjadi uji penerimaan Plan 2. Memori overlay spike (±81 MB) di atas target awal 60 MB; App produksi Plan 2 terukur ±74–77 MB private, dan pemilik produk menaikkan target menjadi < 80 MB (2026-11-12).

**Go/no-go** [keputusan perancang]: hasil kedua spike dilaporkan ke pemilik produk dan disetujui sebelum lanjut ke langkah 3.
- Posisi yang tidak andal pada kombinasi tertentu → dinonaktifkan untuk kombinasi itu (F-07a). Proyek tidak berhenti.
- ETW ternyata butuh `LocalSystem` → diterima, dengan pengamanan S-04/S-05/S-08 tetap berlaku. Keputusan ini dicatat.
- ETW tidak bisa memberi data per proses yang layak → H-04 ditunda, sisanya tetap jalan.

## 12. Pengujian
- **Unit test** (deterministik, tanpa jaringan nyata): perhitungan kecepatan dari counter (termasuk delta negatif, resume, overflow), format satuan, pemilihan adapter Auto/All/pengecualian, agregasi & retensi (memakai clock palsu, termasuk DST E-15), penjumlahan riwayat per pilihan adapter (H-07), aturan kuota & notifikasi sekali per periode per user, aturan posisi & fallback (dari data koordinat palsu), validasi `settings.json` dan `latest.json`, filter sesi di service, validasi pesan pipe.
- **Integration test**: SQLite (migrasi, WAL, pembersihan, flush saat stop), named pipe App ↔ Service (termasuk batas ukuran, koneksi bersamaan, versi protokol berbeda), verifikasi signature installer.
- **Uji installer**: install/upgrade/uninstall silent di VM Windows 10 dan 11 sebagai admin; App berjalan di akun user biasa.
- **Checklist uji manual** di Windows 10 dan 11 untuk semua kasus §4.2 dan E-01…E-14, termasuk dengan ESET aktif.
- **Uji performa**: jalan 24 jam, ukur CPU/RAM/ukuran DB terhadap target §1.
- Framework test **[BUILDER]**.

## 13. Yang diserahkan ke builder
Struktur solusi & nama proyek, library logging, tool installer, framework test, skema SQLite detail, format angka, interval polling cadangan dan interval ping, provider ETW spesifik, desain visual jendela Settings dan popup grafik (asal mengikuti tema Windows dan aksesibel).

## 14. Ringkasan asumsi yang perlu dicek pemilik produk
- Windows 10 minimum 22H2 (C-01).
- Skala 1000 untuk bit/s, 1024 untuk byte/s (F-02).
- Adapter VPN dikecualikan dari mode All secara default (F-05).
- Notifikasi kuota di 80% dan 100%; kuota mengikuti pilihan adapter (H-01).
- Hotkey default `Ctrl+Alt+N` (X-03).
- Data per aplikasi tidak disimpan ke disk, hanya sesi berjalan dan hari ini (S-02).
- Pengguna kantor bukan admin (C-04).
- Saat upgrade, App di sesi aktif baru jalan lagi di login berikutnya (§8).
- Installer self-contained (§8).
- Sertifikat berlaku 3 tahun, timestamp dari server publik (§9).
