# Plan 4 — Integrasi App ↔ Service Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** App memakai data service: jendela **riwayat** (live 60 detik, 24 jam, 7 hari; H-02), **kuota** harian/bulanan dengan notifikasi 80%/100% sekali per periode per user (H-01), dan jendela **pemakaian per aplikasi** (Session/Today, "approximate"; S-05, S-09). Tanpa service, semuanya tampil "Not available" dan overlay tetap normal (S-06).

**Architecture:** Logika murni (backoff, periode kuota, evaluasi notifikasi, state notifikasi, deret grafik, buffer live, skala sumbu) di `NetworkSpeed.Core`, dengan TDD. App memakai `ServiceClient` (Plan 3) lewat satu `ServiceConnection` yang tidak pernah memblokir UI thread. Grafik digambar sendiri di WPF (`Polyline` di `Canvas`), tanpa paket chart.

**Tech Stack:** C# / .NET 10, WPF, xUnit v3. **Tidak ada paket NuGet baru.**

**Spec:** `docs/superpowers/specs/2026-11-11-network-speed-design.md` (H-01, H-02, H-07, S-02, S-05, S-06, S-09, §5, §7).

## Global Constraints

- Semua constraint Plan 1–3 tetap berlaku (TFM, `TreatWarningsAsErrors`, InvariantCulture, tanpa commit kecuali diminta, tanpa atribusi AI).
- App **tidak pernah** membuka `history.db`; semua data lewat `ServiceClient` dengan `requireSessionZeroServer: true` (Plan 3).
- Panggilan pipe selalu `async` dengan timeout (3 detik) dan **tidak pernah** di UI thread secara blocking. Kegagalan apa pun → state "Not available", tanpa dialog teknis.
- Reconnect memakai backoff 1 s → 2 s → 5 s → 10 s → 30 s (maks), lalu kembali ke 1 s setelah berhasil (S-06).
- Pengaturan kuota ditambahkan ke `UserSettings` sebagai field baru **dengan default**; `SchemaVersion` tetap 1, dan file lama tetap terbaca (kompatibilitas mundur, spec §5).
- Status notifikasi kuota disimpan terpisah di `%LOCALAPPDATA%\NetworkSpeed\state.json` (bukan di `settings.json`, supaya mengedit settings tidak mereset status). Kunci yang berumur > 62 hari dibuang.
- Kuota dihitung dari adapter pilihan user (F-04) yang **ada di daftar service**, maksimal 64 ID per request (`PipeProtocol.MaxAdapterIds`).
- Teks UI bahasa Inggris. Angka per aplikasi selalu diberi label "approximate". Nama proses hanya ditampilkan, tidak dicatat ke log.
- Target RAM UI < 80 MB private (spec §1), diukur ulang setelah jendela grafik dibuka/ditutup.

## Review Focus

1. **Service berhenti/restart saat jendela riwayat/per aplikasi terbuka** → tampil "Not available", lalu pulih sendiri tanpa exception atau UI macet. Dipin di Task 1 (`Backoff_*`) dan Task 6 (`ServiceConnection` dengan klien palsu: `Refresh_ServiceDown_ThenUp_Recovers`).
2. **Batas periode kuota**: tanggal mulai 31 di Februari, tengah malam, DST, awal bulan sebelum tanggal mulai → rentang benar. Dipin di Task 2.
3. **Notifikasi sekali per periode** walau App restart, batas kuota diubah di tengah periode, atau pemakaian langsung melompat > 100% → tepat satu notifikasi per ambang per (periode, batas). Dipin di Task 3.
4. **Riwayat berlubang** (sleep, service mati) dan nilai sangat besar/nol → grafik tidak crash, celah = 0, sumbu Y tetap masuk akal. Dipin di Task 4.
5. **Pilihan adapter tidak cocok dengan service** (Specific yang tidak ada, All dengan > 64 adapter, tidak ada adapter) → request dibatasi atau tampil "No adapter selected", bukan error. Dipin di Task 5.

**Urutan eksekusi:** 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10.

---

## Struktur file

```
src/NetworkSpeed.Core/
  Ipc/ReconnectBackoff.cs
  Quota/QuotaPeriod.cs
  Quota/QuotaEvaluator.cs
  Storage/NotificationStateStore.cs
  Display/HistorySeries.cs               # deret kontinu (celah = 0) + downsample per jam
  Display/LiveHistoryBuffer.cs           # ring buffer 60 sampel
  Display/ChartScale.cs                  # "nice max" sumbu Y
  Ipc/HistoryRequestFactory.cs           # ID adapter pilihan user → request (≤ 64), rentang waktu
  Settings/UserSettings.cs               # + QuotaDailyGb, QuotaMonthlyGb, QuotaMonthStartDay
  Settings/SettingsValidator.cs          # + aturan kuota
  Settings/SettingsSerializer.cs         # + baca field kuota
src/NetworkSpeed.App/
  Features/ServiceConnection.cs
  Features/QuotaMonitor.cs
  History/HistoryWindow.xaml(.cs)
  History/TrafficChart.cs                # FrameworkElement: dua Polyline + sumbu
  PerApp/PerAppWindow.xaml(.cs)
  Settings/SettingsWindow.xaml(.cs)      # + grup Quota
  App.xaml.cs                            # menu History…, Per-app usage…
tests/NetworkSpeed.Core.Tests/{Ipc,Quota,Storage,Display}/...
tests/NetworkSpeed.App.Tests/            # proyek baru (net10.0-windows, referensi App): ServiceConnectionTests
```

---

### Task 1: `ReconnectBackoff` (S-06)

**Files:** Create `src/NetworkSpeed.Core/Ipc/ReconnectBackoff.cs`; Test `tests/NetworkSpeed.Core.Tests/Ipc/ReconnectBackoffTests.cs`

**Interfaces — Produces:** `public sealed class ReconnectBackoff { public TimeSpan NextDelay(); public void Reset(); }`, dengan urutan 1, 2, 5, 10, 30, 30, … detik.

- [ ] **Step 1: Test yang gagal**: `Backoff_Sequence_CapsAt30s`, `Backoff_Reset_StartsAgain`.
- [ ] **Step 2: FAIL → Step 3: implementasi → Step 4: PASS + suite penuh.**

---

### Task 2: Periode kuota (H-01, Review Focus 2)

**Files:** Create `src/NetworkSpeed.Core/Quota/QuotaPeriod.cs`; Test `tests/NetworkSpeed.Core.Tests/Quota/QuotaPeriodTests.cs`

**Interfaces — Produces:**
- `public enum QuotaKind { Daily, Monthly }`
- `public sealed record QuotaWindow(QuotaKind Kind, DateOnly From, DateOnly To, string Key);`. `From`/`To` inklusif. `Key` = `daily:2026-11-12` atau `monthly:2026-11-01`, unik per periode.
- `public static class QuotaPeriod { public static QuotaWindow Daily(DateTime localNow); public static QuotaWindow Monthly(DateTime localNow, int startDay); }`

**Aturan:** periode bulanan mulai pada `startDay` (1–31). Kalau bulan itu lebih pendek, mulai di hari terakhir bulan itu. Kalau hari ini < tanggal mulai bulan ini, periodenya dimulai di bulan sebelumnya. `To` = sehari sebelum mulai periode berikutnya. Panjangnya selalu ≤ 31 hari (≤ 62, cocok untuk `GetDailyTotals`).

- [ ] **Step 1: Test yang gagal**:

```csharp
[Fact] public void Daily_IsToday()                                   // 2026-11-12 10:00 → From=To=2026-11-12, Key "daily:2026-11-12"
[Fact] public void Monthly_Start1_IsCalendarMonth()                  // 2026-11-12 → 11-01..11-30
[Fact] public void Monthly_BeforeStartDay_UsesPreviousMonth()        // start 15, 2026-11-12 → 10-15..11-14
[Fact] public void Monthly_Start31_InFebruary_ClampsToMonthEnd()     // start 31, 2027-02-20 → 01-31..02-27; 2027-02-28 → 02-28..03-30
[Fact] public void Monthly_AtMidnightOfStartDay_NewPeriod()          // start 15, 2026-11-15 00:00 → 11-15..12-14
[Theory] public void Monthly_NeverLongerThan31Days(...)              // semua startDay 1..31 × 24 bulan
```

- [ ] **Step 2: FAIL → Step 3: implementasi → Step 4: PASS + suite penuh.**

---

### Task 3: Evaluasi notifikasi + state store (H-01, Review Focus 3)

**Files:** Create `src/NetworkSpeed.Core/Quota/QuotaEvaluator.cs`, `src/NetworkSpeed.Core/Storage/NotificationStateStore.cs`; Test `tests/NetworkSpeed.Core.Tests/Quota/QuotaEvaluatorTests.cs`, `Storage/NotificationStateStoreTests.cs`

**Interfaces — Produces:**
- `public sealed record QuotaAlert(QuotaKind Kind, int Percent, ulong UsedBytes, ulong LimitBytes, string StateKey);`
- `public static class QuotaEvaluator { public static IReadOnlyList<QuotaAlert> Evaluate(QuotaWindow window, ulong usedBytes, ulong limitBytes, IReadOnlySet<string> alreadyNotified); }`. Ambang 80 dan 100. `StateKey` = `{window.Key}:{limitBytes}:{percent}`, sehingga mengubah batas mempersenjatai ulang notifikasi untuk batas baru. Pemakaian yang langsung ≥ 100% hanya menghasilkan alert 100 (80 ikut ditandai terkirim, tidak ditampilkan). `limitBytes == 0` → tidak ada alert.
- `public sealed class NotificationStateStore(string path, TimeProvider time) { IReadOnlySet<string> Load(); void MarkSent(IEnumerable<string> keys); }`. JSON `{ "sent": { "<key>": "<yyyy-MM-dd>" } }`. Kunci > 62 hari dibuang saat save. File rusak → kosong + rename `.corrupt-*` (pola E-11). Tulis atomik.

- [ ] **Step 1: Test yang gagal**: `Evaluate_Below80_None`, `Evaluate_Cross80_Once` (kedua kali dengan key terkirim → kosong), `Evaluate_JumpTo120_Only100Shown_80MarkedToo`, `Evaluate_LimitChanged_Rearms`, `Evaluate_ZeroLimit_None`, `Store_RoundTrip_SurvivesRestart`, `Store_PrunesOlderThan62Days`, `Store_CorruptFile_StartsEmpty`.
- [ ] **Step 2: FAIL → Step 3: implementasi → Step 4: PASS + suite penuh.**

---

### Task 4: Deret grafik, buffer live, skala sumbu (H-02, Review Focus 4)

**Files:** Create `src/NetworkSpeed.Core/Display/HistorySeries.cs`, `LiveHistoryBuffer.cs`, `ChartScale.cs`; Tests di `tests/NetworkSpeed.Core.Tests/Display/`

**Interfaces — Produces:**
- `public sealed record SeriesPoint(DateTime LocalTime, ulong SentBytes, ulong ReceivedBytes);`
- `public static class HistorySeries { public static IReadOnlyList<SeriesPoint> PerMinute(IReadOnlyList<HistoryPoint> points, DateTime fromLocal, DateTime toLocal); public static IReadOnlyList<SeriesPoint> PerHour(IReadOnlyList<HistoryPoint> points, DateTime fromLocal, DateTime toLocal); }`. Setiap menit/jam di rentang ada tepat satu titik; yang tidak ada di data = 0. Titik dengan `LocalTime` tidak valid diabaikan.
- `public sealed class LiveHistoryBuffer(int capacity = 60) { void Add(double up, double down); IReadOnlyList<(double Up, double Down)> Snapshot(); }`
- `public static class ChartScale { public static double NiceMax(double max); }` → 1, 2, 5 × 10ⁿ ≥ max; `max ≤ 0` → 1.

- [ ] **Step 1: Test yang gagal**: `PerMinute_FillsGapsWithZero` (titik 10:00 & 10:03 → 4 titik), `PerMinute_IgnoresMalformedTime`, `PerHour_SumsMinutesIntoHours` (24 jam → 24 titik), `PerHour_SevenDays_Is168Points`, `Live_KeepsLast60`, `NiceMax_Cases` (Theory: 0→1, 0.3→0.5, 7→10, 1234→2000, 1e12→1e12).
- [ ] **Step 2: FAIL → Step 3: implementasi → Step 4: PASS + suite penuh.**

---

### Task 5: Request riwayat dari pilihan adapter (H-07, Review Focus 5)

**Files:** Create `src/NetworkSpeed.Core/Ipc/HistoryRequestFactory.cs`; Test `tests/NetworkSpeed.Core.Tests/Ipc/HistoryRequestFactoryTests.cs`

**Interfaces — Produces:**
- `public static class HistoryRequestFactory`
  - `public static IReadOnlyList<Guid>? AdapterIds(IReadOnlyList<AdapterInfo> serviceAdapters, AdapterSelection selection)`. Pakai `AdapterSelector` atas daftar **service**. Kosong → `null` (UI: "No adapter selected"). Lebih dari 64 → 64 pertama, urut ID.
  - `public static PipeRequest Minutes(IReadOnlyList<Guid> ids, DateTime fromLocal, DateTime toLocal)`
  - `public static PipeRequest DailyTotals(IReadOnlyList<Guid> ids, DateOnly from, DateOnly to)`. Hasilnya selalu lolos `PipeRequestValidator`.

- [ ] **Step 1: Test yang gagal**: `AdapterIds_Auto_UsesServiceList`, `AdapterIds_SpecificMissing_Null`, `AdapterIds_Over64_Capped`, `Minutes_7Days_PassesValidator` (10.080 titik), `DailyTotals_Month_PassesValidator`.
- [ ] **Step 2: FAIL → Step 3: implementasi → Step 4: PASS + suite penuh.**

---

### Task 6: `ServiceConnection` di App (S-06, Review Focus 1)

**Files:** Create `src/NetworkSpeed.App/Features/ServiceConnection.cs`; buat proyek test `tests/NetworkSpeed.App.Tests` (net10.0-windows, `UseWPF`, referensi App; masuk slnx) dengan `ServiceConnectionTests.cs`

**Interfaces — Produces:**
- `public interface IServiceTransport { Task<PipeResponse> SendAsync(PipeRequest request, CancellationToken ct); }`. Produksi: adapter tipis atas `ServiceClient`.
- `public sealed class ServiceConnection(IServiceTransport transport, ReconnectBackoff backoff, TimeProvider time) : IDisposable`
  - `bool IsAvailable`, `bool IsPerAppAvailable`, `IReadOnlyList<AdapterInfo> Adapters`
  - `event Action? StateChanged`
  - `Task RefreshAsync(CancellationToken ct)`: `GetStatus` + `GetAdapters`. Gagal → `IsAvailable=false`, jadwal ulang sesuai backoff. Berhasil → reset backoff, lalu cek ulang setiap 30 detik.
  - `Task<PipeResponse> QueryAsync(PipeRequest r, CancellationToken ct)`: diteruskan. `Unavailable` → menandai tidak tersedia dan memicu refresh.
  - `void Start()`: loop `RefreshAsync` di background memakai `PeriodicTimer`/`Task.Delay`; `StateChanged` di-marshal ke dispatcher pemanggil.

- [ ] **Step 1: Test yang gagal** (transport palsu yang bisa diset turun/naik, `FakeTime`): `Refresh_ServiceUp_Available`, `Refresh_ServiceDown_ThenUp_Recovers`, `Query_Unavailable_FlipsState`, `Refresh_EtwOff_PerAppUnavailable`.
- [ ] **Step 2: FAIL → Step 3: implementasi → Step 4: PASS + suite penuh.**

---

### Task 7: Jendela riwayat (H-02)

**Files:** Create `src/NetworkSpeed.App/History/HistoryWindow.xaml(.cs)`, `History/TrafficChart.cs`; Modify `App.xaml.cs` (menu "History…", `LiveHistoryBuffer` diisi dari `ReadingUpdated`)

**Interfaces — Consumes:** Task 1–6. **Produces:**
- `TrafficChart : FrameworkElement`, dengan properti `Series` (`IReadOnlyList<(DateTime? Time, double Up, double Down)>`), `UnitMode`, dan `YMax`. Menggambar dua garis (↑ dan ↓, warna dari `SystemColors`/tema), garis sumbu, dan label maks memakai `SpeedFormatter`. Seluruhnya di `OnRender`, tanpa paket chart. `AutomationProperties.Name` berisi ringkasan, misalnya "Download peak 5.4 MB/s".
- `HistoryWindow`: tab **Live (60 s)** (dari buffer App, tanpa service), **24 hours** (per menit → 1.440 titik), dan **7 days** (per jam → 168 titik). Total ↑/↓ periode ditampilkan di bawah. Tab 24 jam/7 hari: kalau service tidak ada → "History is not available (service not running)"; kalau tidak ada adapter → "No adapter selected". Refresh tiap 60 detik selama terbuka; request dibatalkan saat jendela ditutup.

- [ ] **Step 1: Verifikasi manual** dengan service dev terpasang: ketiga tab berisi data; hentikan service (admin) → pesan "not available" dalam ≤ 30 detik tanpa macet; start lagi → pulih.
- [ ] **Step 2: Ukur RAM** setelah membuka/menutup jendela 5× (target < 80 MB private). Catat di ledger.
- [ ] **Step 3: Build `-warnaserror` + suite penuh + format.**

---

### Task 8: Kuota (H-01)

**Files:** Modify `src/NetworkSpeed.Core/Settings/UserSettings.cs`, `SettingsValidator.cs`, `SettingsSerializer.cs` (+ test); Create `src/NetworkSpeed.App/Features/QuotaMonitor.cs`; Modify `SettingsWindow.xaml(.cs)` (grup Quota), `App.xaml.cs` (tooltip baris kuota)

**Interfaces — Produces:**
- `UserSettings`: `double? QuotaDailyGb` (null = mati; 0,1–100.000), `double? QuotaMonthlyGb` (null = mati; 0,1–1.000.000), `int QuotaMonthStartDay` (default 1; 1–31). GB = 1024³ byte [keputusan plan: konsisten dengan skala byte 1024].
- `QuotaMonitor`: tiap 5 menit dan saat start. Untuk setiap kuota aktif, ambil `GetDailyTotals` (adapter dari Task 5) → jumlahkan ↑+↓ → `QuotaEvaluator` → balloon "Daily data limit: 80% used (4.0 of 5.0 GB)" → `MarkSent`. Properti `Summary` untuk tooltip, misalnya "Today: 4.0 / 5.0 GB · Month: 40 / 100 GB". Service tidak ada → tanpa notifikasi, dan `Summary` berisi "Quota: not available".

- [ ] **Step 1: Test yang gagal** (Core): `Deserialize_OldFileWithoutQuota_Defaults` (kompatibilitas mundur), `Normalize_QuotaOutOfRange_ClampedOrNull`, `RoundTrip_QuotaFields`.
- [ ] **Step 2: FAIL → Step 3: implementasi → Step 4: PASS + suite penuh.**
- [ ] **Step 5: Verifikasi manual**: set kuota harian sedikit di bawah pemakaian hari ini → satu balloon 80% atau 100%; restart App → tidak muncul lagi; ubah batas → muncul sekali lagi.

---

### Task 9: Jendela per aplikasi (S-02, S-05, S-09)

**Files:** Create `src/NetworkSpeed.App/PerApp/PerAppWindow.xaml(.cs)`; Modify `App.xaml.cs` (menu "Per-app usage…")

**Interfaces — Produces:** `PerAppWindow`: pilihan Session/Today, daftar (Name, ↑, ↓, Total dengan `TooltipBuilder.FormatBytes`), dan baris "System" yang dibedakan, misalnya "System (other users and services)". Label tetap "Values are approximate and scaled to the adapter total." Refresh tiap 5 detik selama terbuka. Service atau ETW tidak tersedia → pesan yang sesuai. Kolom bisa diurutkan, dan semua kontrol punya nama aksesibel.

- [ ] **Step 1: Verifikasi manual**: buka saat mengunduh → aplikasi pengunduh di atas; ganti Session/Today; hentikan service → pesan "not available".
- [ ] **Step 2: Build + suite + format.**

---

### Task 10: Dokumentasi, ukur, uji penerimaan

**Files:** Modify `README.md` (fitur riwayat/kuota/per aplikasi, `state.json`), `CLAUDE.md` bila perlu

- [ ] **Step 1: Perbarui README.**
- [ ] **Step 2: Ukur RAM/CPU App** 10 menit idle dengan service aktif dan setelah jendela dibuka/ditutup (target < 80 MB, < 1% CPU). Laporkan ke pemilik produk bila terlampaui.
- [ ] **Step 3: Review akhir** oleh reviewer independen, lalu fix pass sesuai skill executing-plans.

---

### Task 11: Proses viewer terpisah untuk riwayat & per aplikasi (spec §1, §3)

**Alasan:** setelah jendela riwayat/per aplikasi dibuka, App tertahan 88–102 MB (JIT + cache WPF), di atas target < 80 MB. Pemilik produk memilih "kurangi lebih jauh" (2026-11-12): jendela tersebut dipindah ke proses pendek umur.

**Desain:**
- Exe yang sama dijalankan dengan argumen `--viewer history|perapp --parent <pid> [--live <up:down;…>]`. Mode viewer bercabang di awal `OnStartup`, **sebelum** mutex single instance; tidak membuat overlay, tray, hotkey, atau kuota.
- Viewer keluar saat jendelanya ditutup **atau** saat proses induk (App utama) keluar.
- Satu proses per jenis jendela. Klik menu lagi saat viewer masih hidup → jendelanya dibawa ke depan, bukan proses baru.
- Data Live 60 detik terakhir dikirim lewat argumen (maks 60 sampel, nilai harus finite ≥ 0; rusak → dibuang, bukan error), lalu viewer menyampel sendiri (`AppHost` dengan `canSaveSettings: false`).
- Viewer membaca `settings.json` (read-only, tidak pernah menulis). Perubahan settings saat viewer terbuka berlaku saat viewer dibuka lagi.
- Log viewer ke `viewer-history.log` / `viewer-perapp.log` (bukan `app.log`) agar dua proses tidak berebut file dan rotasi.
- Viewer berbicara langsung ke service lewat `ServiceClient` (cek sesi 0 tetap berlaku). Sebelum cek status pertama selesai, jendela menampilkan "Loading…", bukan "not available".

**Files:**
- Create `src/NetworkSpeed.Core/Viewer/ViewerCommandLine.cs`, `src/NetworkSpeed.App/Viewer/ViewerLauncher.cs`, `src/NetworkSpeed.App/Viewer/ViewerSession.cs`, `src/NetworkSpeed.Interop/Shell/ForegroundWindow.cs`
- Modify `RollingFileLog` (parameter `baseName`), `ServiceConnection` (`FirstCheck`), `HistoryWindow`, `PerAppWindow`, `App.xaml.cs`
- Test `tests/NetworkSpeed.Core.Tests/Viewer/ViewerCommandLineTests.cs`, `RollingFileLogTests`, `ServiceConnectionTests`

**Interfaces — Produces:**
- `enum ViewerKind { History, PerApp }`
- `sealed record ViewerArguments(ViewerKind Kind, int ParentProcessId, IReadOnlyList<(double Up, double Down)> Live)`
- `static class ViewerCommandLine { bool IsViewer(IReadOnlyList<string>); ViewerArguments? Parse(IReadOnlyList<string>); IReadOnlyList<string> Build(ViewerArguments) }`
- `RollingFileLog(string directory, long maxBytes = 1_048_576, int maxFiles = 5, TimeProvider? time = null, string baseName = "app")`
- `ServiceConnection.FirstCheck : Task`, selesai setelah `RefreshAsync` pertama, baik service tersedia maupun tidak.

- [ ] **Step 1: Test `ViewerCommandLineTests`.** Roundtrip; jenis tidak dikenal / parent hilang / parent ≤ 0 → null; live rusak, NaN, negatif → kosong; > 60 sampel → 60 terakhir; argumen biasa (`--autostart`) → bukan viewer. RED, lalu implementasi, GREEN.
- [ ] **Step 2: Test `RollingFileLog_BaseName_*`.** Tulis `viewer-history.log`, rotasi `viewer-history.1.log`; baseName tidak valid → `ArgumentException`. RED, lalu implementasi, GREEN.
- [ ] **Step 3: Test `FirstCheck_*`.** Belum selesai sebelum refresh; selesai setelah refresh walau service mati. RED, lalu implementasi, GREEN.
- [ ] **Step 4: App.** Launcher di proses utama; mode viewer; jendela menunggu `FirstCheck`; hapus jendela berat & `ReleaseMemory` dari proses utama.
- [ ] **Step 5: Verifikasi.**
  - Suite, build `-warnaserror`, format.
  - UIA: menu History… / Per-app usage… membuka jendela di proses lain; klik ulang membawa ke depan; tutup jendela membuat proses keluar; keluar dari App membuat viewer ikut keluar.
  - Ukur private memory App utama sebelum dan sesudah membuka/menutup jendela (target < 80 MB).
