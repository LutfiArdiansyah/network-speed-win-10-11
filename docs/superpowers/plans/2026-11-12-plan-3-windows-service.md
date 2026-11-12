# Plan 3 — Windows Service Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `NetworkSpeed.Service`, Windows Service yang berjalan sebagai `LocalService` sejak boot:
- mencatat riwayat trafik per adapter per PC ke SQLite (agregat per menit 7 hari, total harian 62 hari);
- mengumpulkan trafik per proses lewat ETW Kernel-Network;
- melayani query read-only lewat named pipe (riwayat, total harian, per aplikasi per sesi pemanggil).

Juga `ServiceClient` di Core yang nanti dipakai App (integrasi UI di Plan 4).

**Architecture:**
- **Logika murni di Core, diuji unit:** delta counter, agregasi menit/hari termasuk DST, retensi, kontrak & validasi protokol pipe, agregasi per proses, filter sesi, dan skala proporsional.
- **`NetworkSpeed.Service` (Worker Service):** berisi SQLite store, sumber ETW, pipe server, dan tiga worker (history, ETW, pipe).
- **Klien pipe:** ada di Core (`System.IO.Pipes`, tanpa dependensi Windows-only selain pipe).

**Tech Stack:** .NET 10 Worker Service, `Microsoft.Extensions.Hosting.WindowsServices` (**paket baru, butuh persetujuan**), `Microsoft.Data.Sqlite` (**paket baru, butuh persetujuan**), `Microsoft.Diagnostics.Tracing.TraceEvent` (sudah disetujui), xUnit v3.

**Spec:** `docs/superpowers/specs/2026-11-11-network-speed-design.md` (S-01…S-09, H-05…H-07, E-05, E-07, E-11, E-15). Hasil spike: `docs/superpowers/spikes/2026-11-12-etw-results.md`.

**Bergantung pada Plan 2:** Task 1 (`IInterfaceCounterSource` dipindah ke Core) dan Task 2 (`IAppLog`, `RollingFileLog`) harus selesai lebih dulu. Selebihnya Plan 3 bisa berjalan paralel dengan Plan 2 karena file yang disentuh berbeda (hanya berbagi `NetworkSpeed.Core`, di folder berbeda).

## Global Constraints

- Semua constraint Plan 1 tetap berlaku.
- Service berjalan sebagai **`NT AUTHORITY\LocalService`**, tanpa grup tambahan dan tanpa perubahan security ETW (S-03).
- Data per PC di `%ProgramData%\NetworkSpeed\` (`history.db`, `logs\`). ACL: tulis hanya untuk LocalService, SYSTEM, Administrators. User biasa **tidak** punya akses (H-05). Folder & ACL dibuat installer (Plan 5); untuk pengembangan, dibuat skrip dev (Task 10).
- Riwayat: agregat **per menit** (retensi 7 hari) dan **total per hari** (retensi 62 hari), per adapter (`InterfaceGuid`), kunci waktu = **waktu lokal PC** (E-15). Ditulis ke disk tiap 1 menit dalam satu transaksi; flush saat service berhenti; WAL (H-06).
- ETW: provider `Microsoft-Windows-Kernel-Network`, keyword 0x30, level Informational. Kirim = ID 10/26/42/58, terima = 11/27/43/59. ID lain (18/34 copied, 14/30 retransmit, connect/disconnect) diabaikan. Loopback diabaikan (S-01).
- Pipe: nama `NetworkSpeed.Service.v1`, ACL Interactive Users (S-1-5-4) read/write, SYSTEM & Administrators full, **deny Network (S-1-5-2)**. Hanya operasi query. Request maks 64 KB, maks 16 koneksi bersamaan, timeout 5 detik per request, respons riwayat maks 10.080 titik (S-04, S-08, H-07).
- Per aplikasi hanya untuk **sesi pemanggil** (`GetNamedPipeClientSessionId`). Sesi 0 dan PID yang tidak diketahui sesinya → satu baris "System" (fail closed). Angka per aplikasi = proporsi × total adapter, label "approximate" (S-05, S-09).
- Parameterized query untuk semua SQL; daftar adapter di `IN` maks 64 item dan tidak boleh kosong.
- Log service tidak memuat IP, port, nama host, atau nama proses (spec §7). Nama proses hanya dikirim lewat pipe ke pemilik sesinya.

## Review Focus

1. **Sleep/resume atau counter adapter reset** di service → tidak ada lonjakan di riwayat. Dipin di Task 1 (`Deltas_CounterDecrease_IsZero`, `Deltas_AfterResetBaseline_NoDelta`) dan Task 8 (`Sampler_ResumeEvent_ResetsBaseline`).
2. **Pergantian DST / jam mundur** → menit yang berulang dijumlahkan ke baris yang sama; jam maju tidak error. Dipin di Task 3 (`Aggregate_RepeatedLocalMinute_SumsIntoSameRow`).
3. **Klien pipe nakal** (request > 64 KB, klien lambat/diam, 50 koneksi sekaligus, JSON rusak, versi berbeda) → ditolak/diputus, service tetap responsif untuk klien lain. Dipin di Task 7 (`Server_OversizedRequest_Rejected`, `Server_SilentClient_TimesOut`, `Server_ManyClients_OthersStillServed`).
4. **Proses keluar sebelum namanya diketahui** → byte masuk "System", tidak pernah ke sesi lain. Dipin di Task 5 (`Fold_UnknownPid_GoesToSystem`).
5. **Disk penuh / database terkunci saat flush** → data menit tetap di memori (maks 24 jam), dicoba lagi pada flush berikutnya, service tidak crash. Dipin di Task 4 (`Flush_StoreThrows_KeepsPendingAndRetries`, `Flush_PendingCapped_DropsOldest`).

---

Catatan penomoran: tidak ada Task 2 (sudah digabung ke Task 1); nomor lain dipertahankan agar rujukan di Review Focus stabil.

## Struktur file

```
src/NetworkSpeed.Core/
  Sampling/CounterDeltaCalculator.cs      # delta byte per adapter; SpeedCalculator dipakai ulang
  History/MinuteKey.cs, History/TrafficBucket.cs
  History/TrafficAggregator.cs            # detik → menit & hari (waktu lokal, E-15)
  History/RetentionPolicy.cs
  History/IHistoryStore.cs
  History/HistoryFlusher.cs               # pending buffer + retry + cap 24 jam
  PerApp/KernelNetworkEvents.cs           # ID event → arah
  PerApp/ProcessTrafficAggregator.cs      # per PID → (session, nama) + baris System
  PerApp/PerAppReportBuilder.cs           # filter sesi + skala proporsional
  Ipc/PipeContracts.cs, Ipc/PipeProtocol.cs, Ipc/PipeRequestValidator.cs
  Ipc/ServiceClient.cs                    # klien untuk App (Plan 4)
src/NetworkSpeed.Service/                 # proyek baru, masuk NetworkSpeed.slnx
  NetworkSpeed.Service.csproj, Program.cs
  Storage/SqliteHistoryStore.cs
  Etw/KernelNetworkEtwSource.cs
  Ipc/PipeServer.cs, Ipc/PipeRequestHandler.cs
  Workers/HistoryWorker.cs, Workers/EtwWorker.cs, Workers/PipeWorker.cs
  Platform/ProcessSnapshot.cs             # Process.GetProcesses() → (pid → session, nama)
tests/NetworkSpeed.Core.Tests/{Sampling,History,PerApp,Ipc}/...
tests/NetworkSpeed.Service.Tests/         # proyek baru: SqliteHistoryStoreTests, PipeServerTests
tools/dev-service.ps1                     # install/uninstall service untuk pengembangan (dijalankan pengguna sebagai admin)
```

---

### Task 0: Persetujuan dependensi

- [ ] **Step 1: Minta persetujuan pengguna** untuk `Microsoft.Extensions.Hosting.WindowsServices` dan `Microsoft.Data.Sqlite` (versi stabil terbaru). Tanpa persetujuan, Task 4 (store) dan Task 9 (host) tidak bisa dikerjakan; Task 1–3, 5–6 tetap bisa.
- [ ] **Step 2: Tambahkan ke `Directory.Packages.props`** setelah disetujui.

---

### Task 1: `CounterDeltaCalculator` (E-05, E-07)

**Files:**
- Create: `src/NetworkSpeed.Core/Sampling/CounterDeltaCalculator.cs`
- Modify: `src/NetworkSpeed.Core/Sampling/SpeedCalculator.cs` (memakai `CounterDeltaCalculator` di dalamnya; perilaku & test Plan 1 tidak berubah)
- Test: `tests/NetworkSpeed.Core.Tests/Sampling/CounterDeltaCalculatorTests.cs`

**Interfaces:**
- Produces:
  - `public readonly record struct ByteDelta(Guid InterfaceId, ulong SentBytes, ulong ReceivedBytes);`
  - `public sealed class CounterDeltaCalculator { public IReadOnlyList<ByteDelta>? Add(IReadOnlyList<InterfaceCounterSnapshot> snapshots); public void ResetBaseline(); }`. `null` saat belum ada baseline. Delta negatif = 0. Adapter baru dihitung mulai sampel berikutnya.

- [ ] **Step 1: Test yang gagal**: `Deltas_First_Null`, `Deltas_Second_ExactBytes`, `Deltas_CounterDecrease_IsZero`, `Deltas_AfterResetBaseline_NoDelta`, `Deltas_NewInterface_StartsNextSample`.
- [ ] **Step 2: FAIL → Step 3: implementasi + refactor `SpeedCalculator` → Step 4: PASS**, termasuk seluruh `SpeedCalculatorTests` Plan 1 tanpa diubah.
- [ ] **Step 5: Commit (setelah konfirmasi).** `refactor: ekstrak CounterDeltaCalculator`

---

### Task 3: Agregasi menit/hari + retensi (H-05, H-06, E-15)

**Files:**
- Create: `src/NetworkSpeed.Core/History/MinuteKey.cs`, `TrafficBucket.cs`, `TrafficAggregator.cs`, `RetentionPolicy.cs`
- Test: `tests/NetworkSpeed.Core.Tests/History/TrafficAggregatorTests.cs`, `RetentionPolicyTests.cs`

**Interfaces:**
- Produces:
  - `public readonly record struct MinuteKey(Guid AdapterId, string LocalMinute);`, dengan `LocalMinute` berformat `yyyy-MM-ddTHH:mm` (InvariantCulture). `public readonly record struct DayKey(Guid AdapterId, string LocalDay);` (`yyyy-MM-dd`).
  - `public sealed record TrafficBucket<TKey>(TKey Key, ulong SentBytes, ulong ReceivedBytes);`
  - `public sealed class TrafficAggregator { void Add(IReadOnlyList<ByteDelta> deltas, DateTime localNow); IReadOnlyList<TrafficBucket<MinuteKey>> DrainMinutesBefore(DateTime localNow); IReadOnlyList<TrafficBucket<DayKey>> DrainDays(); IReadOnlyList<TrafficBucket<MinuteKey>> DrainAllMinutes(); }`
    - `DrainMinutesBefore`: hanya menit yang sudah lewat (menit berjalan tetap di memori).
    - `DrainAllMinutes`: untuk flush saat stop.
    - Total harian diakumulasi bersamaan dengan menit; `DrainDays` mengembalikan increment sejak drain terakhir (store melakukan upsert-add).
  - `public static class RetentionPolicy { public const int MinuteDays = 7; public const int DailyDays = 62; public static string MinuteCutoff(DateTime localNow); public static string DayCutoff(DateTime localNow); }`. Cutoff menit = `localNow − 7 hari` dibulatkan ke menit; cutoff hari = `localNow.Date − 62 hari`. Keduanya berupa string yang sebanding secara leksikografis dengan kunci.

- [ ] **Step 1: Test yang gagal**:

```csharp
[Fact] public void Aggregate_SameMinute_Sums()
[Fact] public void Aggregate_TwoAdapters_SeparateBuckets()
[Fact] public void Drain_ExcludesCurrentMinute()
[Fact] public void Aggregate_RepeatedLocalMinute_SumsIntoSameRow()   // DST mundur: 02:30 dua kali → satu bucket berisi jumlah keduanya
[Fact] public void Aggregate_SkippedLocalHour_NoError()               // DST maju: 02:00→03:00
[Fact] public void Days_AccumulateAcrossMinutes_AndDrainIsIncremental()
[Fact] public void Aggregate_Midnight_SplitsDays()
[Fact] public void Retention_Cutoffs_SevenAndSixtyTwoDays()            // localNow 2026-11-12T10:15 → "2026-11-05T10:15", "2026-09-11"
```

- [ ] **Step 2: FAIL → Step 3: implementasi → Step 4: PASS + suite penuh.**
- [ ] **Step 5: Commit (setelah konfirmasi).** `feat: agregasi trafik per menit dan per hari`

---

### Task 4: `IHistoryStore`, `HistoryFlusher`, `SqliteHistoryStore` (H-05, H-06, H-07, E-11)

**Files:**
- Create: `src/NetworkSpeed.Core/History/IHistoryStore.cs`, `HistoryFlusher.cs`
- Create: `src/NetworkSpeed.Service/NetworkSpeed.Service.csproj` (Worker SDK `Microsoft.NET.Sdk.Worker`, TFM Windows, referensi Core + Interop), `Storage/SqliteHistoryStore.cs`
- Create: `tests/NetworkSpeed.Service.Tests/` (proyek test baru, masuk slnx), `Storage/SqliteHistoryStoreTests.cs`
- Test: `tests/NetworkSpeed.Core.Tests/History/HistoryFlusherTests.cs`

**Interfaces:**
- Produces:
  - `public interface IHistoryStore { void Write(IReadOnlyList<TrafficBucket<MinuteKey>> minutes, IReadOnlyList<TrafficBucket<DayKey>> days); void Purge(string minuteCutoff, string dayCutoff); IReadOnlyList<HistoryPoint> ReadMinutes(IReadOnlyCollection<Guid> adapters, string fromLocalMinute, string toLocalMinute); IReadOnlyList<HistoryPoint> ReadDays(IReadOnlyCollection<Guid> adapters, string fromLocalDay, string toLocalDay); }`
  - `public sealed record HistoryPoint(string LocalTime, ulong SentBytes, ulong ReceivedBytes);`. Read **menjumlahkan** adapter yang diminta per waktu (H-07) dan diurutkan naik.
  - `public sealed class HistoryFlusher(IHistoryStore store, IAppLog log, int maxPendingMinutes = 1440) { void Enqueue(minutes, days); bool TryFlush(); }`. Gagal → data tetap pending, log warn (tanpa pesan exception), dicoba lagi di panggilan berikutnya. Pending > cap → buang yang tertua + log warn.
  - `public sealed class SqliteHistoryStore(string databasePath, IAppLog log) : IHistoryStore, IDisposable`:
    - Skema v1: `schema_version(version INTEGER)`, `minute_traffic(adapter_id TEXT, minute_local TEXT, sent INTEGER, received INTEGER, PRIMARY KEY(adapter_id, minute_local)) WITHOUT ROWID`, `daily_traffic(adapter_id TEXT, day_local TEXT, sent INTEGER, received INTEGER, PRIMARY KEY(adapter_id, day_local)) WITHOUT ROWID`.
    - `PRAGMA journal_mode=WAL; synchronous=NORMAL`.
    - Write = satu transaksi berisi `INSERT … ON CONFLICT DO UPDATE SET sent = sent + excluded.sent, received = received + excluded.received`.
    - Saat buka: `PRAGMA quick_check` ≠ ok, atau file tidak bisa dibuka sebagai SQLite → rename `history.db.corrupt-yyyyMMddHHmmss` (maks 3) lalu buat baru (E-11).
    - Semua parameter lewat `SqliteParameter`; adapter list 1–64 (selain itu `ArgumentException`).

- [ ] **Step 1: Test yang gagal**:
  - Core `HistoryFlusherTests`: `Flush_Success_ClearsPending`, `Flush_StoreThrows_KeepsPendingAndRetries`, `Flush_PendingCapped_DropsOldest`.
  - Service `SqliteHistoryStoreTests` (DB di folder temp per test): `Write_ThenReadMinutes_SumsSelectedAdapters`, `Write_SameKeyTwice_Adds`, `ReadDays_Range`, `Purge_RemovesOlderThanCutoffs`, `Open_CorruptFile_RenamesAndRecreates`, `Read_EmptyAdapterList_Throws`, `Read_AdapterIdWithQuote_IsParameterized` (Guid saja; pastikan string ber-kutip tidak bisa masuk karena tipe `Guid`), `Open_SetsWalMode`.
- [ ] **Step 2: FAIL → Step 3: implementasi → Step 4: PASS + suite penuh.**
- [ ] **Step 5: Commit (setelah konfirmasi).** `feat: penyimpanan riwayat SQLite dengan flush tahan gagal`

---

### Task 5: Agregasi per proses (S-01, S-02, S-05)

**Files:**
- Create: `src/NetworkSpeed.Core/PerApp/KernelNetworkEvents.cs`, `ProcessTrafficAggregator.cs`
- Test: `tests/NetworkSpeed.Core.Tests/PerApp/ProcessTrafficAggregatorTests.cs`

**Interfaces:**
- Produces:
  - `public enum TrafficDirection { Sent, Received }`
  - `public static class KernelNetworkEvents { public static TrafficDirection? DirectionOf(int eventId); }` (10/26/42/58 → Sent; 11/27/43/59 → Received; lainnya null)
  - `public readonly record struct ProcessIdentity(int SessionId, string ProcessName);` (`SessionId == 0` atau tidak diketahui → `ProcessIdentity.System` = `(0, "System")`)
  - `public sealed class ProcessTrafficAggregator { void Record(int eventId, int pid, long size, bool isLoopback); void Fold(IReadOnlyDictionary<int, ProcessIdentity> snapshot, DateOnly localToday); IReadOnlyDictionary<ProcessIdentity, (ulong Sent, ulong Received)> Session(); IReadOnlyDictionary<ProcessIdentity, (ulong Sent, ulong Received)> Today(); }`
    - `Record` thread-safe, O(1), tanpa alokasi per event (counter per PID di `ConcurrentDictionary<int, long[]>`).
    - `Fold` (dipanggil tiap 5 detik): memindahkan counter per PID ke bucket `ProcessIdentity`. PID yang tidak ada di snapshot → `System`. Ganti hari → `Today` di-reset.
    - Ukuran negatif atau 0 diabaikan.
    - Jumlah identitas dibatasi 4.096; kelebihan masuk ke `(session, "Other")`.

- [ ] **Step 1: Test yang gagal**: `DirectionOf_KnownIds` (Theory 8 ID), `DirectionOf_CopiedAndRetransmit_Null` (18, 34, 14, 30), `Record_Loopback_Ignored`, `Fold_KnownPid_AttributedToIdentity`, `Fold_UnknownPid_GoesToSystem`, `Fold_Session0_GoesToSystem`, `Fold_NewDay_ResetsToday_KeepsSession`, `Record_ConcurrentThreads_NoLostBytes` (8 thread × 10.000 event), `Fold_IdentityCap_OverflowToOther`.
- [ ] **Step 2: FAIL → Step 3: implementasi → Step 4: PASS + suite penuh.**
- [ ] **Step 5: Commit (setelah konfirmasi).** `feat: agregasi trafik per proses dari event Kernel-Network`

---

### Task 6: Laporan per aplikasi + kontrak pipe (S-04, S-05, S-08, S-09, H-07)

**Files:**
- Create: `src/NetworkSpeed.Core/PerApp/PerAppReportBuilder.cs`, `src/NetworkSpeed.Core/Ipc/PipeContracts.cs`, `PipeProtocol.cs`, `PipeRequestValidator.cs`
- Test: `tests/NetworkSpeed.Core.Tests/PerApp/PerAppReportBuilderTests.cs`, `Ipc/PipeRequestValidatorTests.cs`, `Ipc/PipeProtocolTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record PerAppRow(string Name, bool IsSystem, ulong SentBytes, ulong ReceivedBytes);`
  - `public static class PerAppReportBuilder { public static IReadOnlyList<PerAppRow> Build(IReadOnlyDictionary<ProcessIdentity, (ulong Sent, ulong Received)> data, int callerSessionId, ulong adapterSent, ulong adapterReceived); }`
    - Baris = proses milik `callerSessionId`, ditambah satu baris "System" (sesi 0 + proses sesi lain digabung, **tanpa nama**).
    - Skala proporsional per arah: `row = etw_row / etw_total × adapter_total`, dibulatkan sehingga jumlah baris = total adapter.
    - Diurutkan menurun berdasarkan total.
  - Kontrak (JSON, `System.Text.Json` source-generated):
    - `PipeRequest { int ProtocolVersion; string Operation; ... }` dengan Operation ∈ `GetStatus | GetAdapters | GetHistory | GetDailyTotals | GetPerApp`.
    - `GetHistory { Guid[] AdapterIds; string From; string To; HistoryResolution Resolution (Minute|Day) }`
    - `GetDailyTotals { Guid[] AdapterIds; string FromDay; string ToDay }`
    - `GetPerApp { PerAppPeriod Period (Session|Today) }`
    - `PipeResponse { int ProtocolVersion; bool Ok; string? ErrorCode; ... payload }`. `ErrorCode` ∈ `unsupported_version | invalid_request | too_large | internal`, tanpa detail internal.
  - `public static class PipeProtocol { public const string PipeName = "NetworkSpeed.Service.v1"; public const int Version = 1; public const int MaxRequestBytes = 65_536; public const int MaxHistoryPoints = 10_080; public const int MaxAdapterIds = 64; }`. Framing: prefix panjang 4 byte little-endian + UTF-8 JSON.
  - `public static class PipeRequestValidator { public static string? Validate(PipeRequest r); }` → null bila valid, atau `ErrorCode`. Aturan: versi = 1; operation dikenal; adapter 1–64; format waktu valid; `From ≤ To`; jumlah titik rentang ≤ 10.080 (menit) / ≤ 62 (hari).

- [ ] **Step 1: Test yang gagal**: `Build_OnlyCallerSessionNamed_OthersInSystem`, `Build_ScalesToAdapterTotals_SumMatchesExactly`, `Build_ZeroEtwTotal_AllZeroNoDivideByZero`, `Build_SortedDescending`, `Validate_*` (Theory per aturan), `Framing_RoundTrip`, `Framing_LengthOverMax_Rejected`, `Deserialize_GarbageJson_InvalidRequest`.
- [ ] **Step 2: FAIL → Step 3: implementasi → Step 4: PASS + suite penuh.**
- [ ] **Step 5: Commit (setelah konfirmasi).** `feat: kontrak pipe dan laporan per aplikasi`

---

### Task 7: Pipe server + `ServiceClient`

**Files:**
- Create: `src/NetworkSpeed.Service/Ipc/PipeServer.cs`, `Ipc/PipeRequestHandler.cs`, `src/NetworkSpeed.Core/Ipc/ServiceClient.cs`
- Test: `tests/NetworkSpeed.Service.Tests/Ipc/PipeServerTests.cs` (server & klien di proses test yang sama, nama pipe unik per test)

**Interfaces:**
- Consumes: kontrak Task 6, `IHistoryStore`, `ProcessTrafficAggregator`, `PerAppReportBuilder`.
- Produces:
  - `public sealed class PipeServer(string pipeName, IPipeRequestHandler handler, IAppLog log, int maxInstances = 16, TimeSpan? requestTimeout = null)`, dengan `Task RunAsync(CancellationToken ct)`. ACL dibuat via `NamedPipeServerStreamAcl.Create` sesuai Global Constraints. Sesi pemanggil dari `GetNamedPipeClientSessionId` (P/Invoke).
  - `public interface IPipeRequestHandler { PipeResponse Handle(PipeRequest request, int callerSessionId); }`
  - `public sealed class ServiceClient(string pipeName = PipeProtocol.PipeName, TimeSpan? timeout = null)`, dengan method async per operation. Service tidak ada / timeout / versi beda → `ServiceUnavailable` result (bukan exception), untuk S-06. Backoff reconnect dilakukan pemanggil (Plan 4).

- [ ] **Step 1: Test yang gagal**: `Client_GetStatus_RoundTrip`, `Server_OversizedRequest_Rejected` (length prefix 1 MB → koneksi diputus, server tetap melayani klien berikutnya), `Server_SilentClient_TimesOut` (klien connect tanpa kirim → diputus ≤ timeout), `Server_ManyClients_OthersStillServed` (50 klien diam + 1 klien normal sukses), `Server_UnsupportedVersion_ErrorCode`, `Server_HandlerThrows_InternalErrorWithoutDetails`, `Client_NoServer_ReturnsUnavailable`, `Server_PassesCallerSessionId` (handler menerima `Process.GetCurrentProcess().SessionId`), `Server_Acl_DeniesNetwork` (baca `GetAccessControl` pipe → ada rule Deny untuk S-1-5-2).
- [ ] **Step 2: FAIL → Step 3: implementasi → Step 4: PASS + suite penuh.**
- [ ] **Step 5: Commit (setelah konfirmasi).** `feat: pipe server read-only dan ServiceClient`

---

### Task 8: Worker riwayat + worker ETW + snapshot proses

**Files:**
- Create: `src/NetworkSpeed.Service/Workers/HistoryWorker.cs`, `Workers/EtwWorker.cs`, `Etw/KernelNetworkEtwSource.cs`, `Platform/ProcessSnapshot.cs`
- Create: `src/NetworkSpeed.Core/History/HistorySampler.cs` + test (logika loop murni: tick → delta → aggregator → flush tiap menit → purge harian)

**Interfaces:**
- Produces:
  - `public sealed class HistorySampler(IInterfaceCounterSource source, TrafficAggregator aggregator, HistoryFlusher flusher, TimeProvider time)`, dengan `void Tick()` (tiap 1 detik), `void OnResume()`, dan `void Stop()` (drain semua + flush). Adapter yang dicatat: **semua kecuali Loopback** (dan filter interface, yang sudah dibuang `IfTableCounterSource`). Ini keputusan plan: mode Auto bisa memilih adapter `Virtual` (Hyper-V external switch, perbaikan review Plan 1), jadi riwayatnya harus ada. Pengecualian F-05 berlaku saat **memilih** adapter untuk dijumlahkan (dilakukan App lewat `AdapterIds`), bukan saat mencatat. Biaya tambahan hanya beberapa baris per menit.
  - `KernelNetworkEtwSource`: sesi `NetworkSpeed-KernelNetwork` (sesi lama dengan nama sama di-stop dulu), provider + keyword sesuai Global Constraints, callback `(eventId, pid, size, isLoopback)` dari payload `PID`, `size`, `daddr`/`saddr`. Session gagal dibuka → log error + retry tiap 60 detik; per aplikasi melapor "unavailable".
  - `ProcessSnapshot.Read()` → `Dictionary<int, ProcessIdentity>` dari `Process.GetProcesses()` (tanpa membuka handle, S-05).
  - `EtwWorker`: `Fold` tiap 5 detik memakai `ProcessSnapshot`.
  - Power event resume (`SystemEvents` tidak tersedia di service) → pakai `ServiceBase.OnPowerEvent` lewat `WindowsServiceLifetime` turunan, atau deteksi gap waktu > 5 detik antar tick sebagai resume [keputusan plan: deteksi gap, karena tidak bergantung API host].

- [ ] **Step 1: Test yang gagal** (`HistorySamplerTests` dengan fake source/time/store): `Sampler_TicksWithinMinute_NoWrite`, `Sampler_MinuteBoundary_WritesPreviousMinute`, `Sampler_ResumeEvent_ResetsBaseline` (gap waktu 30 detik → tidak ada lonjakan), `Sampler_Stop_FlushesCurrentMinute`, `Sampler_DailyPurge_CalledOncePerDay`, `Sampler_RecordsVirtual_ExcludesLoopback`.
- [ ] **Step 2: FAIL → Step 3: implementasi → Step 4: PASS + suite penuh.**
- [ ] **Step 5: Commit (setelah konfirmasi).** `feat: worker riwayat dan pengumpul ETW`

---

### Task 9: Host service (`Program.cs`) + handler pipe nyata

**Files:**
- Create: `src/NetworkSpeed.Service/Program.cs`, `Workers/PipeWorker.cs`, implementasi `PipeRequestHandler`

**Interfaces:**
- `Program`: `Host.CreateApplicationBuilder` + `AddWindowsService(o => o.ServiceName = "NetworkSpeedService")`; registrasi worker; log ke `%ProgramData%\NetworkSpeed\logs` via `RollingFileLog` (Core), tanpa console logger di produksi.
- `PipeRequestHandler`: `GetStatus` (versi, uptime, etwAvailable), `GetAdapters` (dari `IfTableCounterSource.ReadAdapters`, di-cache 10 detik), `GetHistory`/`GetDailyTotals` (store; menit berjalan dari aggregator ikut dijumlahkan), `GetPerApp` (aggregator + `PerAppReportBuilder` dengan total adapter periode yang sama dari aggregator riwayat).

- [ ] **Step 1: Test yang gagal** `PipeRequestHandlerTests` (Service.Tests, dengan fake store/aggregator): `GetHistory_IncludesCurrentMinuteFromMemory`, `GetPerApp_UsesCallerSession`, `GetPerApp_EtwUnavailable_ErrorCodeUnavailable`.
- [ ] **Step 2: FAIL → Step 3: implementasi → Step 4: PASS + suite penuh.**
- [ ] **Step 5: Commit (setelah konfirmasi).** `feat: host Windows Service`

---

### Task 10: Skrip dev install + uji end-to-end (dijalankan pengguna sebagai admin)

**Files:**
- Create: `tools/dev-service.ps1` (`-Install`: publish ke `%ProgramData%\NetworkSpeed\dev-bin`, buat folder data dengan ACL H-05, `New-Service` dengan akun `NT AUTHORITY\LocalService`, startup Automatic, recovery restart 1 menit × 3 (S-07) via `sc.exe failure`, start. `-Uninstall`: stop, hapus service & dev-bin; data tetap kecuali `-RemoveData`.) Pola yang sama dengan `spikes/EtwSpike/run-etw-spike.ps1` (transcript ke `%ProgramData%\NetworkSpeed\dev-transcript-*.txt`).
- Create: `tools/ServiceProbe/` (konsol, memakai `ServiceClient`: status, adapter, riwayat 60 menit terakhir, per aplikasi sesi ini)

- [ ] **Step 1: Pengguna menjalankan** `tools/dev-service.ps1 -Install` sebagai admin.
- [ ] **Step 2: Verifikasi** dengan `dotnet run --project tools/ServiceProbe` sebagai **user biasa**: status OK, ETW available; setelah 3 menit riwayat berisi ≥ 2 titik; per aplikasi menampilkan proses milik sesi ini + "System"; jumlah per aplikasi = total adapter.
- [ ] **Step 3: Verifikasi keamanan**: user biasa **tidak bisa** membaca `C:\ProgramData\NetworkSpeed\history.db` (`Get-Content` → access denied); `GetPerApp` tidak memuat nama proses sesi lain.
- [ ] **Step 4: Uji ketahanan**: sleep/resume (riwayat tanpa lonjakan); `Stop-Process` paksa pada service → restart otomatis ≤ 1 menit; unduhan besar ±500 MB → catat rasio ETW vs adapter (menutup catatan terbuka spike S-09).
- [ ] **Step 5: Ukur** CPU & RAM service 30 menit (target CPU < 1%, RAM < 50 MB) dan ukuran `history.db`; catat di ledger dan di `docs/superpowers/spikes/2026-11-12-etw-results.md` §0b.
- [ ] **Step 6: Pengguna menjalankan** `-Uninstall` bila diinginkan.
- [ ] **Step 7: Commit (setelah konfirmasi).** `feat: skrip dev service dan ServiceProbe`
