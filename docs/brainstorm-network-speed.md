# Brainstorm — Network Speed di Taskbar

> Status: **brainstorm putaran 2, belum ada implementasi.**
> Setiap poin diberi nomor supaya mudah dikomentari.
> Kolom **Keputusan**: ✅ masuk · ❌ tidak usah · ⚠️ perlu konfirmasi.
> Poin bertanda **[ASSUMPTION]** adalah tebakan saya. Mohon dikoreksi.

---

## 0. Konteks

| # | Hal | Isi |
|---|-----|-----|
| K-01 | Tujuan | Menampilkan kecepatan jaringan (upload/download) di taskbar Windows |
| K-02 | Posisi | Kiri / tengah (setelah app yang di-pin) / kanan; di kiri & kanan setelah item yang sudah ada |
| K-03 | Pengguna | Internal kantor dulu, kemungkinan rilis publik nanti |
| K-04 | OS target | **Windows 10 dan Windows 11** (Q-01) |
| K-05 | Distribusi | **Manual** (Q-02) |
| K-06 | Antivirus kantor | **ESET** (Q-03) |
| K-07 | Bahasa UI | **Inggris saja** (X-04) |

---

## 1. Integrasi taskbar

| # | Poin | Keputusan |
|---|------|-----------|
| T-01 | Windows 11 tidak punya DeskBand API; Windows 10 punya | Info |
| T-03 | **Opsi A: jendela overlay + UI Automation** | ✅ **Dipilih** |
| T-04 | Opsi B: inject ke `explorer.exe` | ❌ |
| T-05 | Posisi dihitung ulang saat pin/unpin, app buka/tutup, perataan taskbar berubah, tray berubah, DPI berubah, Explorer restart | ✅ |
| T-06 | Offset manual sebagai cadangan | ✅ |
| T-08 | Overlay dipakai **sama untuk Windows 10 dan 11** (DeskBand di Windows 10 tidak dipakai) supaya hanya ada satu jalur kode yang perlu diuji | ✅ [ASSUMPTION] usulan saya |

---

## 2. Fitur inti

| # | Fitur | Keputusan |
|---|-------|-----------|
| F-01 | Upload/download real-time via `GetIfTable2` (counter 64-bit) | ✅ |
| F-02 | Satuan bit/s atau byte/s, skala otomatis | ✅ |
| F-03 | Interval refresh 0,5–5 detik, default 1 detik | ✅ |
| F-04 | Pilihan adapter: semua / otomatis / tertentu | ✅ |
| F-05 | Kecualikan adapter virtual (VPN, Hyper-V, WSL, loopback) | ✅ |
| F-06 | Tooltip: adapter, SSID, IP lokal, total sesi | ✅ |
| F-07 | Posisi kiri / tengah / kanan, **disesuaikan dengan kompatibilitas, tidak dipaksakan kalau menyebabkan bug** | ✅ dengan aturan di bawah |
| F-08 | Menu tray / klik kanan | ✅ |

**Aturan F-07 (usulan):**

| # | Aturan |
|---|--------|
| F-07a | Setiap posisi punya status **didukung / tidak didukung** per kombinasi OS + perataan taskbar + orientasi taskbar. Posisi yang tidak didukung **tidak muncul** atau tampil nonaktif di pengaturan, dengan alasan singkat. |
| F-07b | Kalau deteksi posisi gagal saat runtime (struktur taskbar berubah karena Windows update, taskbar penuh), aplikasi **otomatis pindah ke posisi cadangan** (kanan, sebelah kiri tray). Tidak crash dan tidak menimpa ikon lain. |
| F-07c | [ASSUMPTION] Perkiraan awal yang paling stabil adalah posisi **kanan**, yang paling rawan adalah **tengah** di Windows 11. Matriks dukungan final ditentukan setelah prototipe diuji di kedua OS. |

---

## 3. Tampilan

| # | Fitur | Keputusan |
|---|-------|-----------|
| D-01 | Mengikuti tema terang/gelap | ✅ |
| D-02 | Font, ukuran, warna, transparansi | ✅ |
| D-03 | Layout 1 atau 2 baris | ✅ |
| D-04 | Warna berubah saat melewati ambang | ✅ |
| D-05 | Sembunyi saat app fullscreen | ✅ |
| D-06 | Kontras tinggi & aksesibilitas | ✅ |

---

## 4. Data & riwayat (riwayat disimpan 7 hari)

| # | Fitur | Keputusan |
|---|-------|-----------|
| H-01 | Kuota harian/bulanan + notifikasi | ✅ (Q-07) |
| H-02 | Grafik riwayat (popup) | ✅ |
| H-03 | Export CSV | ❌ |
| H-04 | Pemakaian per aplikasi via **Windows Service** | ✅ (Q-06), detail di bagian 4a |
| H-05 | Penyimpanan SQLite, riwayat detail **7 hari**, total harian **62 hari** | ✅ |
| H-06 | Penulisan ke disk dibatch tiap 1 menit | ✅ |

**Detail H-05:**
- Data per detik hanya ada di memori (ring buffer) untuk grafik live.
- Agregat **per menit** disimpan 7 hari (±10.080 baris per adapter, < 1 MB).
- **Total per hari** disimpan 62 hari (±62 baris per adapter) untuk kuota bulanan H-01.
- Data yang melewati masa simpan dihapus otomatis sekali sehari dan saat aplikasi start.
- Ukuran database tetap kecil berapa pun lama aplikasi dipakai, jadi performa tidak menurun.

---

## 4a. Arsitektur Windows Service untuk H-04

| # | Poin | Keputusan |
|---|------|-----------|
| S-01 | Aplikasi dipecah dua: **UI (overlay, user biasa)** dan **service (pengumpul data per aplikasi)** | ✅ |
| S-02 | Data per aplikasi diambil dari **ETW** (provider jaringan kernel), dipetakan ke nama proses | ✅ |
| S-03 | Service berjalan dengan hak **seminimal mungkin**. [ASSUMPTION] Target `LocalService` + anggota grup *Performance Log Users* (cukup untuk sesi ETW); kalau di prototipe ternyata tidak cukup, baru naik ke `LocalSystem`. Perlu dibuktikan di prototipe | ✅ |
| S-04 | Komunikasi UI ↔ service lewat **named pipe** dengan ACL: hanya user lokal yang login yang boleh terhubung, **read-only**, tanpa perintah yang bisa mengubah sistem | ✅ |
| S-05 | **Keamanan multi-user (E-13):** service hanya mengembalikan data proses milik **sesi pemanggil**, supaya user A tidak bisa melihat aplikasi yang dipakai user B | ✅ |
| S-06 | **UI tetap jalan tanpa service.** Kalau service mati atau tidak terpasang, kecepatan total tetap tampil; hanya menu per aplikasi yang menampilkan "Not available" | ✅ |
| S-07 | Service restart otomatis kalau crash (recovery options Windows Service) | ✅ |
| S-08 | Validasi semua pesan dari pipe (ukuran maksimum, format) sebelum diproses | ✅ |

---

## 5. Fitur tambahan

| # | Fitur | Keputusan |
|---|-------|-----------|
| X-01 | Ping/latency ke host yang bisa dikonfigurasi | ✅ |
| X-02 | Notifikasi koneksi putus/tersambung | ✅ |
| X-03 | Hotkey sembunyikan/tampilkan | ✅ |
| X-04 | Bahasa | ✅ **Inggris saja** |
| X-05 | Speed test bawaan | ❌ |

---

## 6. Kesiapan produksi

| # | Poin | Keputusan |
|---|------|-----------|
| P-01 | Autostart & hanya satu instance | ✅ |
| P-02 | Target ringan | ✅, target disesuaikan, lihat Q-04 |
| P-03 | Installer | ✅ untuk distribusi manual, lihat catatan di bawah |
| P-04 | Code signing | ⚠️ lihat Q-08 |
| P-05 | Auto-update | ⚠️ lihat catatan di bawah |
| P-06 | Pengaturan terpusat via GPO | ❌ tidak relevan karena distribusi manual [ASSUMPTION] |
| P-07 | Log crash lokal tanpa data pribadi | ✅ |
| P-08 | Telemetri | ❌ |
| P-09 | Berjalan tanpa admin | ❌. UI tetap tanpa admin; **admin hanya saat install** service (Q-06) |
| P-10 | Uji dengan antivirus kantor | ❌, tapi lihat catatan di bawah |

**Catatan:**
- **P-03 (diperbarui karena Q-06):** installer per-user tidak bisa lagi, karena memasang Windows Service butuh admin. Usulan baru: **installer `.exe` per-machine** (minta admin sekali saat install, ke `Program Files`), yang memasang UI + service, dengan uninstaller yang menghapus service dengan bersih. Versi portable `.zip` ⚠️ masih bisa disediakan, tetapi **tanpa fitur per aplikasi** (S-06). Lihat Q-11.
- **P-05:** dengan distribusi manual, pilihannya (a) **cek versi baru saja** dan menampilkan notifikasi + link unduh, atau (b) tidak ada update sama sekali. Usul saya (a), karena tidak perlu infrastruktur update yang rumit. ⚠️ Butuh lokasi file rilis (mis. shared folder kantor atau URL internal).
- **P-10:** dihapus sesuai keputusan. Catatan saja: karena Opsi A tidak melakukan injection, risiko dianggap virus oleh ESET rendah. Kalau tidak ditandatangani (P-04), ESET/SmartScreen tetap bisa memberi peringatan "unknown publisher".

---

## 7. Kasus tepi

| # | Kasus | Penanganan | Keputusan |
|---|-------|------------|-----------|
| E-01 | Taskbar auto-hide | Overlay ikut muncul/sembunyi | ✅ |
| E-02 | Taskbar atas/samping (Windows 10) | Ikuti aturan F-07a; posisi yang tidak didukung dinonaktifkan | ✅ |
| E-03 | Multi-monitor | Pilihan monitor | ✅ |
| E-04 | Explorer restart | Dengarkan `TaskbarCreated`, hitung ulang posisi | ✅ |
| E-05 | Sleep/resume | Reset baseline counter | ✅ |
| E-06 | Adapter colok/cabut, Wi-Fi ↔ LAN | Daftar adapter diperbarui otomatis | ✅ |
| E-07 | Counter reset karena driver restart | Delta negatif = 0 | ✅ |
| E-08 | Taskbar penuh | Fallback F-07b | ✅ |
| E-09 | DPI berbeda per monitor | Per-Monitor DPI v2 | ✅ |
| E-10 | Tidak ada koneksi | Tampilkan "Offline" | ✅ |
| E-11 | File pengaturan/database rusak | Kembali ke default, backup file yang rusak | ✅ |
| E-12 | Windows update mengubah taskbar | Fallback F-07b | ✅ |
| E-13 | Remote Desktop/multi-user | Satu instance per sesi | ✅ |
| E-14 | Jam sistem mundur/maju (DST, sinkronisasi NTP) | Hitung kecepatan pakai monotonic clock, bukan jam dinding | ✅ usulan baru |

---

## 8. Pertanyaan yang sudah terjawab

| # | Jawaban |
|---|---------|
| Q-01 | Windows 10 dan 11 |
| Q-02 | Manual |
| Q-03 | ESET |
| Q-05 | Semua fitur kecuali yang ditandai ❌ |

---

## 9. Teknologi (Q-04, Q-10)

**Disetujui: C# + .NET 10 (LTS) + WPF, target CPU < 1% saat idle, RAM UI < 60 MB.** Service memakai .NET 10 Worker Service (tanpa UI), target RAM < 40 MB [ASSUMPTION].

| Opsi | Kelebihan | Kekurangan |
|------|-----------|------------|
| **.NET 10 + WPF** ✅ | Matang dan stabil di Windows 10 & 11, akses Win32/UI Automation mudah, jendela transparan didukung, LTS sampai Nov 2028 | RAM ±40–60 MB, lebih besar dari target awal < 30 MB |
| .NET 10 + WinUI 3 | Tampilan paling "Windows 11" | Jendela overlay transparan & topmost masih merepotkan, runtime tambahan, dukungan Windows 10 lebih lemah |
| C++ Win32 + Direct2D | Paling ringan (RAM < 10 MB) | Waktu pengembangan jauh lebih lama, lebih rawan bug memori |

---

## 10. Pertanyaan

**Sudah terjawab:** Q-06 → Windows Service · Q-07 → setuju (7 hari detail, 62 hari total harian) · Q-10 → setuju.

**Masih terbuka:**

| # | Pertanyaan |
|---|------------|
| Q-08 | **P-04: bisa saya generate, dengan batasan.** Saya bisa membuat skrip untuk sertifikat **self-signed** dan menandatangani aplikasi. Tetapi supaya Windows/ESET mempercayainya, sertifikat itu harus **diimpor manual ke setiap PC** (Trusted Publisher & Trusted Root), dan langkah ini harus Anda atau IT yang lakukan. Self-signed **tidak** menghilangkan peringatan SmartScreen di PC yang belum diimpor, dan **tidak bisa dipakai untuk rilis publik** (rilis publik butuh sertifikat berbayar atau Azure Trusted Signing). Pilihan: (a) self-signed untuk internal, (b) minta sertifikat dari CA kantor kalau ada, (c) tidak ditandatangani dulu. |
| Q-09 | Untuk P-05, file rilis versi baru akan ditaruh di mana (shared folder, URL internal, atau tidak perlu cek update)? |
| Q-11 | Versi portable `.zip` (tanpa fitur per aplikasi) tetap disediakan, atau cukup installer saja? |
