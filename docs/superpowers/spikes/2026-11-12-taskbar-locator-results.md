# Hasil spike posisi taskbar (Plan 1, Task 6)

- Status: **sebagian**. Uji otomatis di PC pengembang selesai; uji manual Windows 10, DPI, multi-monitor, dll. **belum** (lihat checklist §5).
- Kode spike (throwaway): `spikes/TaskbarLocatorSpike/` (tidak masuk `NetworkSpeed.slnx`).
- Log runtime spike: `%LOCALAPPDATA%\NetworkSpeedSpike\locator-*.log` (tanpa nama app/IP/SSID).

## 1. Lingkungan uji otomatis

| Item | Nilai |
|---|---|
| OS | Windows 11, build 10.0.26300 |
| Taskbar | bawah, rata tengah, tinggi 48 px, Widgets nonaktif |
| Layar | 1 monitor 1920×1080 (skala 100%, berdasarkan tinggi taskbar 48 px) |
| Jumlah tombol app di taskbar | 20 (pinned + running) |

## 2. Tree UI Automation taskbar (Windows 11, diamati langsung)

```
Pane  cls=Shell_TrayWnd
  Pane  cls=Windows.UI.Input.InputSite.WindowClass
    Pane  id=TaskbarFrame  cls=Taskbar.TaskbarFrameAutomationPeer
      Button  id=StartButton   cls=ToggleButton
      Button  id=SearchButton  cls=ToggleButton
      Button  id="Appid: …"    cls=Taskbar.TaskListButtonAutomationPeer   (× jumlah app)
  Button  id=SystemTrayIcon  cls=SystemTray.*   (Show Hidden Icons, Network, Volume, Clock, Show Desktop)
  Pane  cls=TrayNotifyWnd      (Win32, sejajar area tray)
  Pane  cls=ReBarWindow32 > MSTaskSwWClass   (window lama untuk kompatibilitas; JANGAN dipakai di Win11)
```

- Tombol app dikenali dari `ClassName == "Taskbar.TaskListButtonAutomationPeer"`. `AutomationId` berisi AppUserModelID/path, jadi **jangan dicatat ke log** (bisa memuat path user).
- Tepi kiri tray tersedia dari dua sumber yang konsisten: UIA (`SystemTray.*`, x=1679) dan Win32 `TrayNotifyWnd` (x=1679).
- Windows 10 (belum diverifikasi): tombol app diharapkan berada di `ReBarWindow32 > MSTaskSwWClass > MSTaskListWClass`. Spike sudah memuat jalur ini, tetapi belum diuji.

## 3. Hasil terukur (Win11, rata tengah)

| Posisi | Hasil | x | Detail |
|---|---|---|---|
| Center | ✅ tepat setelah tombol app terakhir | 1453 | `lastAppRight=1445` + margin 8 px |
| Right | ✅ tepat di kiri tray | 1567 | `trayLeft=1679` − lebar overlay − 8 px |
| Left | ✅ pojok kiri (Widgets nonaktif) | 8 | `leftEdge=0` |
| Center, app baru dibuka | ✅ bergeser +22 px otomatis | 1475 | event UIA `StructureChanged` → debounce 250 ms → recalc; `apps=21` |

| Pengukuran | Nilai | Catatan |
|---|---|---|
| Waktu satu kali hitung posisi (UIA) | rata-rata ±100–150 ms, maks 205 ms (n=3) | dijalankan di thread background, UI tidak tertahan |
| CPU spike (Release, 25 detik, termasuk polling 2 detik) | 8,6% dari 1 core ≈ **0,7% total** (12 core) | ❗ masih di atas target idle; lihat §4 |
| Memori spike (Release) | private 81 MB, working set 138 MB | ❗ di atas target UI < 60 MB; lihat §4 |
| Kegagalan UIA | tidak terjadi | jalur fallback Win32 belum terpicu di uji ini |

## 4. Temuan & rekomendasi untuk Plan 2

1. **Pendekatan overlay + UIA layak** di Windows 11 rata tengah: ketiga posisi bisa dihitung dan mengikuti perubahan tombol app lewat event.
2. **Biaya CPU ada di polling penuh UIA tiap 2 detik.** Untuk produksi:
   - polling cukup memakai pengecekan Win32 yang murah (`GetWindowRect` Shell_TrayWnd/TrayNotifyWnd);
   - scan UIA penuh hanya dijalankan bila ada event atau bila rect Win32 berubah;
   - query UIA dipersempit ke `Children` milik `TaskbarFrame`, bukan `Descendants` seluruh tray.
3. **Memori di atas target.** Sebagian besar dari WPF + UIA COM. Di Plan 2: ukur ulang dengan overlay produksi; kalau tetap > 60 MB, pilihannya (a) target dinaikkan, atau (b) pembacaan UIA dipindah ke proses lain. Butuh keputusan pemilik produk.
4. **Left** hanya masuk akal pada Win11 rata tengah. Pada Win11 rata kiri dan Windows 10, pojok kiri ditempati Start/app, jadi Left masuk matriks **tidak didukung** (F-07a), sesuai rancangan.
5. **Taskbar monitor kedua** (`Shell_SecondaryTrayWnd`) belum ditangani spike. Ini perlu di Plan 2 untuk E-03.
6. `AutomationId` tombol app bisa memuat path user, jadi produksi tidak boleh mencatatnya ke log.

## 5. Matriks dukungan F-07a (sementara)

| OS | Perataan / orientasi | Left | Center | Right |
|---|---|---|---|---|
| Win11 | bawah, rata tengah, Widgets off | ✅ teruji | ✅ teruji | ✅ teruji |
| Win11 | bawah, rata tengah, Widgets on | ⏳ | ⏳ | ⏳ |
| Win11 | bawah, rata kiri | ❌ tidak didukung (by design) | ⏳ | ⏳ |
| Win10 | bawah | ❌ tidak didukung (by design) | ⏳ | ⏳ |
| Win10 | atas | ❌ | ⏳ | ⏳ |
| Win10 | kiri/kanan (vertikal) | ❌ | ❌ belum didukung spike | ❌ belum didukung spike → fallback Win32 juga tidak, overlay disembunyikan |

✅ teruji · ⏳ belum diuji · ❌ tidak didukung

## 6. Checklist uji manual (butuh pengguna)

Cara menjalankan di PC yang punya .NET 10 SDK:

```
dotnet run --project spikes/TaskbarLocatorSpike -c Release -- --position=center
```

Untuk PC Windows 10 tanpa SDK, buat versi self-contained lalu salin folder `out/spike` ke PC tersebut:

```
dotnet publish spikes/TaskbarLocatorSpike -c Release -r win-x64 --self-contained -o out/spike
```

Klik kanan overlay untuk ganti posisi (Left/Center/Right), "Recalculate now", atau "Open log folder". Arahkan mouse ke overlay untuk melihat sumber posisi dan waktu hitung.

| # | Skenario | Win11 | Win10 |
|---|---|---|---|
| 1 | Widgets aktif (Win11), cek Left & Center | ☐ | — |
| 2 | Taskbar rata kiri (Win11) | ☐ | — |
| 3 | Taskbar di atas (Win10) | — | ☐ |
| 4 | Taskbar vertikal (Win10): overlay harus tersembunyi, tidak menutupi apa pun | — | ☐ |
| 5 | Skala 150% dan 200% | ☐ | ☐ |
| 6 | 2 monitor dengan skala berbeda | ☐ | ☐ |
| 7 | Auto-hide taskbar: overlay ikut sembunyi/muncul | ☐ | ☐ |
| 8 | Pin/unpin app, buka/tutup ±10 app (Center ikut bergeser) | ☐ | ☐ |
| 9 | Ikon tray bertambah (Right ikut bergeser) | ☐ | ☐ |
| 10 | Restart Explorer (`taskkill /f /im explorer.exe` lalu `start explorer`) | ☐ | ☐ |
| 11 | Sleep lalu resume | ☐ | ☐ |
| 12 | App fullscreen (video/presentasi): overlay sembunyi | ☐ | ☐ |
| 13 | Taskbar penuh (banyak app) di posisi Center: harus pindah ke Right/fallback | ☐ | ☐ |
| 14 | Klik taskbar: overlay tetap di atas taskbar | ☐ | ☐ |
| 15 | Hyper-V external switch / VPN kantor: angka tidak "Offline" | ☐ | ☐ |

Setelah uji, kirim file log terbaru dari `%LOCALAPPDATA%\NetworkSpeedSpike` (tanpa data pribadi) dan centang tabel di atas.

## 7. Uji penerimaan App produksi (Plan 2, Task 13) — status 2026-11-12

Diuji pada `src/NetworkSpeed.App` (bukan spike), Windows 11 build 26300, taskbar rata tengah, 1 monitor 100%:

| Skenario | Hasil |
|---|---|
| Right / Center / Left | ✅ x = 1586 / 1453 / 8 (log `Overlay placement`) |
| #8 buka/tutup app → Center bergeser | ✅ 1453 → 1475 → 1452 |
| Start: UIA gagal sesaat (COMException) | ✅ retry otomatis, Center benar ≤ 1 detik (sebelumnya sempat tampil di Right) |
| Hotkey Ctrl+Alt+N | ✅ hide/show tercatat di log |
| Settings via UI Automation | ✅ 29 kontrol bernama; perubahan posisi langsung diterapkan; disimpan saat ditutup |
| Tooltip | ✅ Adapter, SSID, IPv4, Session, Latency |
| CPU idle | ✅ 0,32% total (30 detik) |
| RAM | ✅ private ±74–77 MB (target dinaikkan ke < 80 MB oleh pemilik produk, 2026-11-12) |

Belum diuji (butuh pengguna, atau mengganggu desktop): skenario #1–7 dan #9–15 pada tabel §6 di Windows 11 & Windows 10, termasuk restart Explorer (#10), sleep/resume (#11), fullscreen (#12), taskbar penuh (#13), Wi-Fi putus untuk notifikasi koneksi, dan hotkey yang bentrok. Gunakan build self-contained (README) di PC Windows 10.
