# Hasil spike ETW per aplikasi (Plan 1, Task 7)

- Status: **selesai** (run #2: A0 00:22, B 00:24–00:58, C 03:29). Satu catatan terbuka: akurasi download pada unduhan besar belum terukur (lihat §0b).

## 0b. Hasil B (LocalService, 30 menit) & C (LocalSystem)

| Item | B | C |
|---|---|---|
| Sesi + provider | ✅ `SESSION-OK` | ✅ `SESSION-OK` (S-1-5-18, elevated) |
| Data terkumpul | ±8,5 menit efektif (47 laporan). Ada jeda 00:32→00:38 dan 00:38→00:58: **PC sleep** | 2 menit |
| Event hilang | 0 | 0 |
| PID tanpa session | maks 1 (proses yang sudah keluar) | maks 1 |
| CPU / memori | turun ke 0,01% saat idle; maks 22 MB | 0,09%; 18 MB |
| Download terbesar per 10 detik | 192 KB (rasio 10 detik 38–142%, fluktuatif pada volume kecil) | 114 KB (95%) |
| Total event (B) | `UDP Datasent` 90.738 · `UDP Datareceived` 12.951 · `TCP Protocolcopieddata…` 3.625 · `TCP Datareceived` 3.452 · `TCP Datasent` 2.848 · lain-lain (connect/disconnect/retransmit) < 500 | — |

Temuan tambahan:
- **Sesi ETW bertahan melewati sleep/resume**: tidak ada error dan tidak ada event hilang, laporan berlanjut setelah bangun. Counter adapter **turun setelah resume** (Wi-Fi tersambung ulang). Perhitungan delta spike mengembalikan 0, persis kasus E-07, jadi aturan "delta negatif = 0 + baseline baru" di Core memang dibutuhkan di service.
- Rasio per 10 detik **tidak stabil** pada volume kecil (event ETW dan counter adapter tidak tercatat di detik yang sama). Rasio hanya bermakna jika diakumulasi (menit/jam). Ini mendukung rekomendasi S-09: proporsi × total adapter.
- **Belum terukur:** unduhan besar (puluhan MB). Selama B tidak ada unduhan besar sebelum PC sleep, sehingga rasio download ±61–75% dari A0/B awal hanya berlaku untuk trafik ringan. Diverifikasi ulang di Plan 3 dengan uji integrasi service. Tidak memblokir go/no-go karena per aplikasi ditampilkan sebagai proporsi.

## 0. Ringkasan hasil run #2 (2026-11-12, Win11 26300, Wi-Fi)

| Pertanyaan | Hasil | Sumber |
|---|---|---|
| **S-03: akun service minimal** | ✅ **`LOCAL SERVICE` bawaan cukup**: tanpa *Performance Log Users*, tanpa izin provider tambahan | A0: `perfLogUsers=False`, `SESSION-OK` |
| S-01: provider & event | `Microsoft-Windows-Kernel-Network`, keyword 0x30. Event yang dipakai: `TCPIP/Datasent.`, `TCPIP/Datareceived.`, `UDPIP/DatasentoverUDPprotocol.`, `UDPIP/DatareceivedoverUDPprotocol.` | EVENTS |
| S-09: rasio ETW vs counter adapter | **upload ±87–96%**, **download ±61–75%** | REPORT `ratio` |
| S-05: atribusi sesi | ✅ semua PID dapat session ID (`unknownSessionPids=0`) lewat `Process.GetProcesses()` | REPORT |
| Porsi trafik sesi 0 ("System") | 94–99% di PC ini: satu layanan sesi 0 mengunggah terus ±0,3 MB/s via UDP | REPORT `session0`, `top` |
| Event hilang | 0 | `lost=0` |
| Biaya | CPU 0,08–0,3% total, private memory 16–21 MB | REPORT |

**Kenapa download lebih rendah:** ETW mencatat **payload** TCP/UDP, sedangkan counter adapter mencatat seluruh frame (header IP/TCP/802.11, ACK, retransmit, trafik non-TCP/UDP). Pada download dengan banyak paket kecil, porsi header lebih besar. Selain itu, event `TCPIP/Protocolcopieddataonbehalfofuser.` belum dihitung; perlu dicek apakah event ini tumpang tindih dengan `Datareceived` (Plan 3).

**Rekomendasi S-09:** tampilkan per aplikasi sebagai **proporsi**. `bytes_app = (etw_app / etw_total) × total_adapter`, sehingga jumlah per aplikasi sama dengan angka overlay, dan tetap diberi label "approximate".

**Temuan run #1 (dikoreksi):** akun user non-elevated (anggota *Performance Log Users*) memang ditolak untuk provider ini. Tetapi `LOCAL SERVICE` diizinkan oleh security default Windows, jadi kesimpulan sementara di §1 (bahwa LocalService + PLU tidak cukup) **tidak berlaku**.

Bug spike yang ditemukan dan sudah diperbaiki (penting untuk Plan 3):
- Nama event upload adalah `…Datasent`, bukan "Send". Pencocokan berdasarkan nama event rapuh, jadi produksi sebaiknya memakai **ID event** atau nama task + opcode.
- `ProcessIdToSessionId` butuh handle proses dan gagal untuk `LOCAL SERVICE` (`-1`). Session ID diambil dari snapshot sistem (`Process.GetProcesses()` / `NtQuerySystemInformation`) tanpa membuka handle.
- Kode spike (throwaway): `spikes/EtwSpike/` (tidak masuk `NetworkSpeed.slnx`), skrip: `spikes/EtwSpike/run-etw-spike.ps1`.
- Provider: `Microsoft-Windows-Kernel-Network`, GUID `7dd42a49-5329-4832-8dfd-43d979153a88` (dibaca dari TraceEvent, bukan ditebak). Keyword 0x30 (IPv4 + IPv6).
- Log berisi PID, session ID, dan angka saja; tidak ada nama proses, IP, atau port.

## 1. Temuan awal (tanpa admin, akun pengembang)

| Uji | Hasil |
|---|---|
| Token: anggota *Performance Log Users* (aktif), Administrators deny-only (UAC) | — |
| Membuat sesi ETW + provider `Microsoft-Windows-DotNETRuntime` | ✅ berhasil |
| Membuat sesi ETW + provider **`Microsoft-Windows-Kernel-Network`** | ❌ `UnauthorizedAccessException` 0x80070005 |
| Security descriptor khusus untuk provider Kernel-Network di registry | tidak ada (Windows memakai default) |

**Kesimpulan sementara:** keanggotaan *Performance Log Users* cukup untuk membuat sesi, tetapi **tidak cukup untuk mengaktifkan provider Kernel-Network**. Asumsi awal S-03 (LocalService + Performance Log Users) kemungkinan besar **tidak cukup**. Ada dua jalan yang diuji di varian B dan C:

- **B. LocalService + izin khusus.** Installer memberi izin `TRACELOG_GUID_ENABLE` pada provider Kernel-Network khusus untuk akun service (lewat `EventAccessControl`). Hak service tetap minimal, tetapi installer mengubah security ETW di PC.
- **C. LocalSystem.** Paling sederhana, tetapi hak service paling tinggi. Mitigasinya tetap S-04/S-05/S-08 (pipe read-only, filter per sesi, validasi input).

## 2. Langkah untuk pengguna

1. Buka **PowerShell "Run as administrator"** di root repo.
2. Jalankan (±35 menit; varian B 30 menit):
   ```
   powershell -ExecutionPolicy Bypass -File spikes\EtwSpike\run-etw-spike.ps1 -Run
   ```
3. **Selama varian B berjalan**, lakukan berurutan:
   - unduh file besar (≥ 500 MB) lewat browser selama ±5 menit;
   - biarkan aktivitas sistem jalan (mis. buka Windows Update → Check for updates);
   - biarkan PC idle ±15 menit.
4. Setelah selesai, bersihkan semua perubahan sistem:
   ```
   powershell -ExecutionPolicy Bypass -File spikes\EtwSpike\run-etw-spike.ps1 -Uninstall
   ```
5. Kirim file `%ProgramData%\NetworkSpeedSpike\etw-*.log` (isinya hanya angka, PID, dan session ID).

Perubahan sistem yang dibuat `-Run` dan dibatalkan `-Uninstall`:
- folder `%ProgramData%\NetworkSpeedSpike`;
- `LOCAL SERVICE` ditambahkan ke grup *Performance Log Users* (dicabut hanya kalau skrip yang menambahkannya);
- izin enable provider Kernel-Network untuk `LOCAL SERVICE` (varian B);
- scheduled task `\NetworkSpeedSpike\*`.

## 3. Yang diukur dari log (diisi setelah run)

| Pertanyaan | Baris log | A | B | C |
|---|---|---|---|---|
| Sesi + provider bisa dibuka? | `SESSION-OK` / `SESSION-FAILED` | ✅ (A0, tanpa hak tambahan) | ✅ | ✅ |
| Rasio ETW vs counter adapter (S-09) | `ratio sent/recv` | — | ⏳ | ⏳ |
| Porsi trafik sesi 0 → baris "System" (S-05) | `session0=` | — | ⏳ | ⏳ |
| Event hilang saat trafik tinggi | `lost=` | — | ⏳ | ⏳ |
| CPU & memori | `cpu=` `privMB=` | — | ⏳ | ⏳ |
| Event yang tidak dikenali | `unknownDir=` `malformed=` | — | ⏳ | ⏳ |

## 4. Keputusan yang akan diambil di go/no-go (Task 8)

- **S-03:** akun service final. B (LocalService + izin provider) bila berhasil, C (LocalSystem) bila B gagal.
- **S-01:** provider final dan keyword.
- **H-04:** dilanjutkan bila rasio ETW vs adapter masuk akal dan event hilang dapat diterima; selain itu ditunda (spec §11).

## 0c. Service produksi (Plan 3, 2026-11-12)

- Terpasang via `tools/dev-service.ps1`: `NetworkSpeedService`, akun `NT AUTHORITY\LocalService` **tanpa hak tambahan**, sesi 0, start otomatis, recovery restart 60 detik × 3.
- RAM private ±21–25 MB (target < 50 MB).
- `ServiceProbe` sebagai user biasa (klien mode ketat, server wajib sesi 0): status OK, ETW aktif, riwayat per menit terbaca, per aplikasi berisi proses sesi sendiri + baris System.
- ACL data per PC (H-05): user biasa ditolak membaca `history.db` dan log, menulis `dev-bin`, maupun membaca ACL folder.
- Restart setelah crash: ✅ dimatikan paksa (admin) 08:00:19 → SCM menyalakan ulang 08:01:21 (PID baru); riwayat sebelumnya utuh. User biasa tidak bisa mematikan proses service (Access denied).
- Sleep/resume: ✅ Modern Standby 08:02→08:05; riwayat kosong selama sleep, tanpa lonjakan saat resume.
- Unduhan besar: ✅ ±1,9 GB (IDMan) 08:05–08:08, puncak ±938 MB/menit; per aplikasi menunjukkan IDMan ±97,6% trafik, dan jumlah baris = total adapter. Rasio ETW mentah vs adapter tidak terlihat lewat pipe (sudah diskalakan); bila perlu angka mentah, tambah diagnostik di service (Plan 4/5).
- CPU service idle 0,00%, RAM private ±23 MB.
