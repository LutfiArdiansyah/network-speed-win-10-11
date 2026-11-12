# Plan 2 — App Overlay (WPF) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Aplikasi `NetworkSpeed.App` yang berjalan per user tanpa admin: overlay kecepatan live di taskbar (Left/Center/Right) sesuai matriks F-07c, tray icon, jendela Settings, tampilan (D-01…D-06), ping, notifikasi koneksi, hotkey, autostart, dan multi-monitor. **Tanpa** service, riwayat, kuota, dan per aplikasi (Plan 3–4).

**Architecture:** Logika murni (sampler, settings, teks overlay, perhitungan posisi, debounce koneksi, parsing hotkey, pemilihan monitor) ada di `NetworkSpeed.Core` dan diuji unit. Pembacaan Win32/UIA (layout taskbar, shell state, SSID, ping) ada di `NetworkSpeed.Interop`. `NetworkSpeed.App` (WPF + WinForms `NotifyIcon`) hanya merangkai dan menampilkan. Algoritma posisi diangkat dari spike `spikes/TaskbarLocatorSpike`, tetapi ditulis ulang dengan TDD.

**Tech Stack:** C# / .NET 10, WPF, WinForms `NotifyIcon` (bawaan WindowsDesktop, tanpa paket baru), `System.Windows.Automation` (UIAutomationClient bawaan), CsWin32, xUnit v3. **Tidak ada paket NuGet baru.**

**Spec:** `docs/superpowers/specs/2026-11-11-network-speed-design.md` (status GO 2026-11-12). Hasil spike: `docs/superpowers/spikes/2026-11-12-taskbar-locator-results.md`.

## Global Constraints

- Semua constraint Plan 1 tetap berlaku (TFM, `TreatWarningsAsErrors`, InvariantCulture, monotonic clock, adapter by `Guid`, tanpa injection/hook global/DeskBand, commit butuh konfirmasi, tanpa atribusi AI).
- App berjalan **tanpa admin**, satu instance per sesi user (spec §6, E-13).
- Teks UI dalam bahasa Inggris (C-07).
- Pengaturan per user di `%LOCALAPPDATA%\NetworkSpeed\settings.json` dengan `schemaVersion`; nilai tidak valid dikembalikan ke default **per field** (spec §5).
- Log per user di `%LOCALAPPDATA%\NetworkSpeed\logs\`, rotasi 5 file × 1 MB; **tidak mencatat** IP, SSID, host ping, nama proses, atau `AutomationId` tombol taskbar (spec §7, F-07d).
- Polling posisi hanya memakai Win32; scan UIA hanya saat event atau saat rect Win32 berubah, selalu di thread background, query dibatasi ke `Children` milik `TaskbarFrame` (spec T-05).
- Posisi overlay tidak boleh menutupi tombol app. Kalau ruang tidak cukup: fallback ke Right, lalu disembunyikan dengan alasan (F-07b, T-10).
- Target performa: CPU UI < 1% idle, RAM UI < 80 MB private (spec §1, diperbarui 2026-11-12). Kalau terlampaui, laporkan ke pemilik produk, jangan diubah diam-diam.
- Format data `settings.json` adalah kontrak ke depan: field baru wajib punya default; field yang tidak dikenal diabaikan tanpa error.

## Review Focus

1. **Explorer restart saat overlay tampil** → overlay kembali di posisi benar ≤ 1 detik setelah taskbar muncul, tanpa crash. Dipin di Task 4 (`Calculate_NullLayout_IsHiddenWithReason`) dan checklist penerimaan Task 13 (#10).
2. **`settings.json` diedit manual** dengan nilai di luar rentang, tipe salah, atau field asing → field itu saja yang kembali ke default, field lain tetap. Dipin di Task 2 (`Load_WrongTypeForOneField_OnlyThatFieldDefaults`, `Load_UnknownField_Ignored`).
3. **Monitor pilihan dicabut** → overlay pindah ke taskbar utama, tidak hilang. Dipin di Task 4 (`SelectMonitors_SpecificMissing_FallsBackToPrimary`).
4. **Hotkey bentrok/tidak valid** → aplikasi tetap jalan, Settings menampilkan peringatan. Dipin di Task 5 (`TryParse_InvalidGestures_ReturnFalse`) dan Task 11.
5. **DPI tinggi atau taskbar penuh** sehingga overlay tidak muat → disembunyikan dengan alasan, tidak menimpa ikon. Dipin di Task 4 (`Calculate_NoRoomAnywhere_HiddenNotOverlapping`).

**Urutan eksekusi** (karena tipe dipakai bersama): 1 → 5 → 4 → 2 → 3 → 6 → 7 → 8 → 9 → 10 → 11 → 12 → 13. `PixelOffset`, `OverlayPosition`, dan `MonitorMode` didefinisikan di Task 4 dan dipakai `UserSettings` di Task 2.

---

## Struktur file

```
src/NetworkSpeed.Core/
  Sampling/IInterfaceCounterSource.cs      # DIPINDAH dari Interop (namespace NetworkSpeed.Core.Sampling)
  Sampling/AdapterSelection.cs
  Sampling/SpeedReading.cs
  Sampling/SpeedSampler.cs
  Settings/UserSettings.cs                 # record + enum pendukung
  Settings/SettingsValidator.cs
  Settings/SettingsSerializer.cs
  Storage/SettingsFileStore.cs
  Logging/IAppLog.cs
  Logging/RollingFileLog.cs
  Display/OverlayTextBuilder.cs
  Display/LatencyFormatter.cs
  Taskbar/PixelRect.cs
  Taskbar/TaskbarLayout.cs
  Taskbar/OverlayPosition.cs
  Taskbar/PositionSupportEvaluator.cs
  Taskbar/PlacementCalculator.cs
  Taskbar/MonitorSelector.cs
  Connectivity/ConnectivityDebouncer.cs
  Input/HotkeyGesture.cs
src/NetworkSpeed.Interop/
  IfTableCounterSource.cs                  # implementasi tetap; interface pindah ke Core
  Taskbar/TaskbarLayoutReader.cs
  Taskbar/TaskbarChangeMonitor.cs
  Shell/ShellState.cs
  Wlan/WlanSsidReader.cs
  Net/LatencyProbe.cs
src/NetworkSpeed.App/                      # proyek baru, masuk NetworkSpeed.slnx
  NetworkSpeed.App.csproj, app.manifest, App.xaml(.cs)
  Composition/AppHost.cs
  Platform/SingleInstance.cs, Platform/GlobalHotkey.cs, Platform/AutostartGate.cs
  Overlay/OverlayWindow.xaml(.cs), Overlay/OverlayCoordinator.cs
  Tray/TrayIconController.cs
  Settings/SettingsWindow.xaml(.cs), Settings/SettingsViewModel.cs
  Notifications/BalloonNotifier.cs
tests/NetworkSpeed.Core.Tests/            # test baru per kelas Core di atas
tests/NetworkSpeed.Interop.Tests/          # TaskbarLayoutReaderTests, ShellStateTests
```

---

### Task 1: Pindahkan `IInterfaceCounterSource` ke Core + `SpeedSampler`

**Files:**
- Move: `src/NetworkSpeed.Interop/IInterfaceCounterSource.cs` → `src/NetworkSpeed.Core/Sampling/IInterfaceCounterSource.cs` (namespace `NetworkSpeed.Core.Sampling`; dokumentasi remarks tetap)
- Modify: `src/NetworkSpeed.Interop/IfTableCounterSource.cs` (using), `tools/SpeedProbe/Program.cs`
- Create: `src/NetworkSpeed.Core/Sampling/AdapterSelection.cs`, `SpeedReading.cs`, `SpeedSampler.cs`
- Test: `tests/NetworkSpeed.Core.Tests/Sampling/SpeedSamplerTests.cs`

**Interfaces:**
- Consumes: `SpeedCalculator`, `AdapterSelector`, `IMonotonicClock` (Plan 1).
- Produces:
  - `public sealed record AdapterSelection(AdapterSelectionMode Mode, Guid? SpecificId, bool IncludeVpnInAll);`
  - `public sealed record SpeedReading(double UpBytesPerSecond, double DownBytesPerSecond, bool IsOffline, IReadOnlyList<AdapterInfo> SelectedAdapters, ulong SessionSentBytes, ulong SessionReceivedBytes);`
  - `public sealed class SpeedSampler(IInterfaceCounterSource source, IMonotonicClock clock, TimeSpan adapterRefreshInterval)`
    - `SpeedReading? Sample(AdapterSelection selection)`: `null` saat baseline (sampel pertama atau setelah resume), kecuali offline.
    - `void InvalidateAdapters()`: dipanggil saat `NetworkChange`.
    - `void OnResume()`: reset baseline + invalidate adapter (E-05).

**Aturan:** adapter dibaca ulang hanya bila `InvalidateAdapters()` dipanggil atau sudah lewat `adapterRefreshInterval` (remarks `ReadAdapters`: mahal). Total sesi = akumulasi delta byte × elapsed dari kecepatan yang dihitung, bukan nilai counter mentah (counter bisa reset).

- [ ] **Step 1: Tulis test yang gagal** memakai `FakeCounterSource` (counter & adapter bisa diatur) dan `FakeClock` di `tests/NetworkSpeed.Core.Tests/Fakes/`:

```csharp
[Fact] public void Sample_First_ReturnsNullBaseline()
[Fact] public void Sample_Second_SumsSelectedAdaptersOnly()          // 2 adapter, Auto memilih 1 → hanya 1 dijumlah
[Fact] public void Sample_NoSelectedAdapter_ReturnsOffline()         // IsOffline == true, kecepatan 0, tidak null
[Fact] public void Sample_AdaptersCachedUntilRefreshInterval()       // ReadAdaptersCalls == 1 setelah 5 sampel dalam 9 s (interval 10 s)
[Fact] public void InvalidateAdapters_ForcesReadOnNextSample()       // ReadAdaptersCalls == 2
[Fact] public void OnResume_NextSampleIsBaseline()                   // Sample() == null setelah OnResume
[Fact] public void Sample_SessionTotals_AccumulateAcrossCounterReset() // counter turun → total sesi tidak berkurang
```

- [ ] **Step 2: Jalankan → FAIL.** `dotnet test --project tests/NetworkSpeed.Core.Tests --filter-class "*SpeedSamplerTests"`
- [ ] **Step 3: Pindahkan interface + implementasikan `SpeedSampler`.** Perbarui `IfTableCounterSource`, SpeedProbe, dan spike yang mereferensikan interface (spike boleh dibiarkan rusak? **Tidak**: perbarui using spike agar tetap ter-build).
- [ ] **Step 4: Jalankan → PASS**, lalu `dotnet test` penuh → semua lolos.
- [ ] **Step 5: Commit (setelah konfirmasi).** `refactor: pindahkan IInterfaceCounterSource ke Core dan tambah SpeedSampler`

---

### Task 2: Pengaturan pengguna + file store + log

**Files:**
- Create: `src/NetworkSpeed.Core/Settings/UserSettings.cs`, `SettingsValidator.cs`, `SettingsSerializer.cs`, `src/NetworkSpeed.Core/Storage/SettingsFileStore.cs`, `src/NetworkSpeed.Core/Logging/IAppLog.cs`, `RollingFileLog.cs`
- Test: `tests/NetworkSpeed.Core.Tests/Settings/SettingsSerializerTests.cs`, `Storage/SettingsFileStoreTests.cs`, `Logging/RollingFileLogTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record UserSettings` dengan field dan default berikut (nama properti persis):

    | Properti | Tipe | Default | Rentang/aturan |
    |---|---|---|---|
    | `SchemaVersion` | int | 1 | — |
    | `UnitMode` | `SpeedUnitMode` | `BytesPerSecond` | enum |
    | `RefreshIntervalMs` | int | 1000 | 500–5000 |
    | `AdapterMode` | `AdapterSelectionMode` | `Auto` | enum |
    | `SpecificAdapterId` | Guid? | null | — |
    | `IncludeVpnInAll` | bool | false | — |
    | `Position` | `OverlayPosition` | `Right` | enum |
    | `OffsetLeft`, `OffsetCenter`, `OffsetRight` | `PixelOffset(int X, int Y)` | (0,0) | X,Y −200…200 |
    | `Layout` | `OverlayLayout { OneLine, TwoLines }` | `TwoLines` | enum |
    | `FontFamily` | string | "Segoe UI" | 1–64 char, selain itu default |
    | `FontSize` | double | 11 | 8–16 |
    | `UpColor`, `DownColor`, `AlertColor` | string? | null, null, "#FF4040" | `#RRGGBB`/`#AARRGGBB`; null = warna tema |
    | `BackgroundOpacity` | double | 0 | 0–1 |
    | `Theme` | `ThemeMode { System, Light, Dark }` | `System` | enum |
    | `AlertThresholdBytesPerSecond` | double? | null | > 0 atau null |
    | `HideOnFullscreen` | bool | true | — |
    | `MonitorMode` | `MonitorMode { PrimaryOnly, All, Specific }` | `PrimaryOnly` | enum |
    | `SpecificMonitorId` | string? | null | ≤ 256 char |
    | `PingEnabled` | bool | true | — |
    | `PingHost` | string? | null (= default gateway) | hostname/IP valid ≤ 253 char, selain itu null |
    | `PingIntervalMs` | int | 5000 | 1000–60000 |
    | `ShowLatencyOnOverlay` | bool | false | — |
    | `ConnectivityNotifications` | bool | true | — |
    | `Hotkey` | string | "Ctrl+Alt+N" | harus lolos `HotkeyGesture.TryParse`, selain itu default |
    | `AutostartEnabled` | bool | true | — |

  - `public static class SettingsSerializer { public static UserSettings Deserialize(string json); public static string Serialize(UserSettings s); }`. Deserialisasi **per field** dari `JsonDocument` (bukan `JsonSerializer.Deserialize<UserSettings>` langsung), supaya satu field yang salah tipe tidak menggagalkan field lain. Field asing diabaikan. JSON yang tidak bisa di-parse sama sekali → `SettingsFormatException`.
  - `public static class SettingsValidator { public static UserSettings Normalize(UserSettings s); }` (clamp/default per aturan tabel).
  - `public enum SettingsLoadOutcome { Loaded, Missing, CorruptRecovered }`
  - `public sealed class SettingsFileStore(string path, TimeProvider time) { (UserSettings Settings, SettingsLoadOutcome Outcome) Load(); void Save(UserSettings s); }`. Save atomik (tulis `*.tmp` lalu `File.Move(overwrite: true)`). Corrupt → rename ke `settings.json.corrupt-yyyyMMddHHmmss`, simpan maks 3 file corrupt terbaru (E-11).
  - `public interface IAppLog { void Info(string m); void Warn(string m); void Error(string m, Exception? ex = null); }`
  - `public sealed class RollingFileLog(string directory, long maxBytes = 1_048_576, int maxFiles = 5, TimeProvider? time = null) : IAppLog`. Exception dicatat hanya `GetType().Name` + `HResult` + stack trace (tanpa `Message`, karena pesan exception bisa memuat path/IP).

- [ ] **Step 1: Tulis test yang gagal**, antara lain:

```csharp
[Fact] public void Deserialize_EmptyObject_AllDefaults()                      // == new UserSettings()
[Fact] public void Load_WrongTypeForOneField_OnlyThatFieldDefaults()          // {"RefreshIntervalMs":"fast","FontSize":14} → 1000 & 14
[Fact] public void Load_UnknownField_Ignored()                                // {"Foo":1,"FontSize":12} → FontSize 12
[Fact] public void Normalize_OutOfRange_Clamped()                              // RefreshIntervalMs 50 → 500; FontSize 99 → 16; offset X 999 → 200
[Fact] public void Normalize_InvalidColor_BecomesDefault()                     // "red" → null (Up), "#FF4040" (Alert)
[Fact] public void Normalize_InvalidHotkey_BecomesDefault()                    // "Ctrl+" → "Ctrl+Alt+N"
[Fact] public void RoundTrip_PreservesAllFields()
[Fact] public void Load_MissingFile_ReturnsDefaultsAndMissing()
[Fact] public void Load_GarbageFile_RenamesCorruptAndReturnsDefaults()         // file .corrupt-* ada, Outcome CorruptRecovered
[Fact] public void Load_FourthCorrupt_KeepsOnlyThreeNewest()
[Fact] public void Save_ThenLoad_ReturnsSameSettings()
[Fact] public void RollingFileLog_ExceedsMax_RotatesAndKeepsFiveFiles()
[Fact] public void RollingFileLog_Error_DoesNotWriteExceptionMessage()        // Message "C:\\secret\\path" tidak muncul di file
```

- [ ] **Step 2: Jalankan → FAIL.**
- [ ] **Step 3: Implementasikan** tipe di Interfaces. `HotkeyGesture.TryParse` dari Task 5 belum ada; urutan task: kerjakan Task 5 Step 1–4 sebelum Step 3 ini, atau stub validasi hotkey dengan test yang menunggu Task 5 (pilih urutan: **Task 5 dikerjakan sebelum Task 2**).
- [ ] **Step 4: Jalankan → PASS** + `dotnet test` penuh.
- [ ] **Step 5: Commit (setelah konfirmasi).** `feat: pengaturan pengguna, file store, dan log berotasi`

---

### Task 3: Teks overlay + format latency (F-06, D-03, D-04, E-10, X-01)

**Files:**
- Create: `src/NetworkSpeed.Core/Display/OverlayTextBuilder.cs`, `LatencyFormatter.cs`
- Test: `tests/NetworkSpeed.Core.Tests/Display/OverlayTextBuilderTests.cs`

**Interfaces:**
- Consumes: `SpeedReading` (Task 1), `UserSettings` (Task 2), `SpeedFormatter` (Plan 1).
- Produces:
  - `public sealed record LatencyReading(TimeSpan? RoundTrip);` (`null` = timeout)
  - `public sealed record OverlayText(IReadOnlyList<string> Lines, bool IsUpAlert, bool IsDownAlert);`
  - `public static class OverlayTextBuilder { public static OverlayText Build(SpeedReading? reading, UserSettings settings, LatencyReading? latency); }`
  - `public static class LatencyFormatter { public static string Format(LatencyReading? latency); }` → `"12 ms"`, `"<1 ms"`, `"timeout"`, `"—"` (null/belum ada).

**Aturan:** TwoLines → `["↑ 1.2 MB/s", "↓ 5.4 MB/s"]`; OneLine → `["↑ 1.2 MB/s  ↓ 5.4 MB/s"]`. `reading == null` → nilai `0.0`. Offline → `["Offline"]` (OneLine maupun TwoLines). `ShowLatencyOnOverlay` hanya berlaku di OneLine (ditambahkan `"  12 ms"`); di TwoLines diabaikan [keputusan plan: menjaga lebar dua baris tetap stabil]. Alert = kecepatan arah itu > `AlertThresholdBytesPerSecond`.

- [ ] **Step 1: Tulis test yang gagal**: `Build_TwoLines_FormatsBothDirections`, `Build_OneLine_SingleLine`, `Build_NullReading_ShowsZero`, `Build_Offline_ShowsOffline`, `Build_AboveThreshold_SetsAlertPerDirection` (up 2 MB/s, down 10 KB/s, threshold 1 MB/s → IsUpAlert true, IsDownAlert false), `Build_LatencyOnlyInOneLine`, `LatencyFormatter_Cases` (Theory: 12.4 ms → "12 ms", 0.3 ms → "<1 ms", null RoundTrip → "timeout", null → "—").
- [ ] **Step 2: FAIL → Step 3: implementasi → Step 4: PASS + suite penuh.**
- [ ] **Step 5: Commit (setelah konfirmasi).** `feat: teks overlay dan format latency`

---

### Task 4: Perhitungan posisi, matriks dukungan, pemilihan monitor (F-07, F-07a–c, T-06, T-10, E-03)

**Files:**
- Create: `src/NetworkSpeed.Core/Taskbar/PixelRect.cs`, `TaskbarLayout.cs`, `OverlayPosition.cs`, `PositionSupportEvaluator.cs`, `PlacementCalculator.cs`, `MonitorSelector.cs`
- Test: `tests/NetworkSpeed.Core.Tests/Taskbar/PlacementCalculatorTests.cs`, `PositionSupportEvaluatorTests.cs`, `MonitorSelectorTests.cs`

**Interfaces:**
- Produces:
  - `public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom) { int Width; int Height; }`
  - `public readonly record struct PixelOffset(int X, int Y);` (dipakai juga oleh `UserSettings`; buat di task ini bila Task 2 belum jalan, atau di Task 2, cukup satu definisi di `Taskbar/`)
  - `public enum OverlayPosition { Left, Center, Right }`, `public enum TaskbarEdge { Bottom, Top, Left, Right }`
  - `public sealed record TaskbarLayout(string MonitorId, bool IsPrimary, PixelRect Taskbar, TaskbarEdge Edge, bool IsWindows11, bool IsCentered, bool HasUiaDetail, int? WidgetsRight, int? FirstButtonLeft, int? LastAppRight, int TrayLeft);`. `HasUiaDetail == false` berarti hanya data Win32 (fallback F-07b).
  - `public sealed record PositionSupport(OverlayPosition Position, bool IsSupported, string? Reason);`
  - `public static class PositionSupportEvaluator { public static IReadOnlyList<PositionSupport> Evaluate(TaskbarLayout layout); }`
  - `public enum PlacementSource { Requested, FallbackRight, Hidden }`
  - `public sealed record Placement(bool IsVisible, int X, int Y, OverlayPosition? Effective, PlacementSource Source, string Reason);`
  - `public static class PlacementCalculator { public const int Margin = 8; public static Placement Calculate(TaskbarLayout? layout, OverlayPosition requested, int width, int height, PixelOffset offset); }`
  - `public enum MonitorMode { PrimaryOnly, All, Specific }`
  - `public static class MonitorSelector { public static IReadOnlyList<TaskbarLayout> Select(IReadOnlyList<TaskbarLayout> layouts, MonitorMode mode, string? specificMonitorId); }`

**Aturan (port dari spike, F-07c):**
- `layout == null` → Hidden, reason "Taskbar not found".
- Edge Left/Right (vertikal) → Hidden, reason "Vertical taskbar is not supported".
- Y = `Taskbar.Top + (Taskbar.Height − height) / 2`.
- **Right:** X = `TrayLeft − width − Margin`; tidak muat bila `LastAppRight` ada dan X < `LastAppRight + Margin`.
- **Center:** butuh `HasUiaDetail` dan `LastAppRight`; X = `LastAppRight + Margin`; tidak muat bila `X + width + Margin > TrayLeft`.
- **Left:** didukung hanya bila `IsWindows11 && IsCentered && HasUiaDetail`; X = `(WidgetsRight ?? Taskbar.Left) + Margin`; batas kanan = `FirstButtonLeft`; tidak muat bila `X + width + Margin > batas`.
- Urutan: requested → Right (FallbackRight) → Hidden ("Not enough space on the taskbar"). Tidak pernah menimpa tombol app (T-10).
- Offset posisi efektif ditambahkan setelah perhitungan, lalu di-clamp agar overlay tetap di dalam `Taskbar`.
- `PositionSupportEvaluator`: Left tidak didukung kecuali Win11+centered (reason "Left corner is used by Start on this taskbar"); semua tidak didukung untuk taskbar vertikal; Center tidak didukung bila `!HasUiaDetail`.
- `MonitorSelector`: PrimaryOnly → yang `IsPrimary`; All → semua; Specific → yang `MonitorId` cocok, **fallback ke primary** bila tidak ada. Bila tidak ada primary sama sekali, kembalikan elemen pertama.

- [ ] **Step 1: Tulis test yang gagal** dengan layout dari hasil spike (`Taskbar=(0,1032,1920,1080)`, Win11 centered, `FirstButtonLeft=476`, `LastAppRight=1445`, `TrayLeft=1679`, overlay 104×34):

```csharp
[Fact] public void Calculate_Right_PlacesLeftOfTray()            // X 1567, Y 1039, Source Requested
[Fact] public void Calculate_Center_PlacesAfterLastApp()         // X 1453
[Fact] public void Calculate_Left_NoWidgets_PlacesAtLeftEdge()   // X 8
[Fact] public void Calculate_Left_WithWidgets_PlacesAfterWidgets() // WidgetsRight 150 → X 158
[Fact] public void Calculate_Left_OnWindows10_FallsBackRight()   // Source FallbackRight, X 1567
[Fact] public void Calculate_Center_NoRoom_FallsBackRight()      // LastAppRight 1600 → Center gagal; Right juga gagal → lihat test berikut
[Fact] public void Calculate_NoRoomAnywhere_HiddenNotOverlapping() // LastAppRight 1600 → Hidden, reason berisi "space"
[Fact] public void Calculate_Win32OnlyLayout_RightStillWorks()    // HasUiaDetail false, LastAppRight null → Right X 1567
[Fact] public void Calculate_Win32OnlyLayout_CenterFallsBackRight()
[Fact] public void Calculate_VerticalTaskbar_Hidden()
[Fact] public void Calculate_NullLayout_IsHiddenWithReason()
[Fact] public void Calculate_Offset_AppliedAndClampedInsideTaskbar() // offset (10,-2) → X 1577, Y 1037; offset (500,0) → X+width ≤ 1920
[Fact] public void Calculate_TopTaskbar_CentersVertically()       // Taskbar (0,0,1920,40) Edge Top → Y 3
[Fact] public void Evaluate_Win11Centered_AllSupported()
[Fact] public void Evaluate_Win11LeftAligned_LeftUnsupportedWithReason()
[Fact] public void Evaluate_Vertical_AllUnsupported()
[Fact] public void SelectMonitors_SpecificMissing_FallsBackToPrimary()
[Fact] public void SelectMonitors_All_ReturnsEvery()
```

- [ ] **Step 2: FAIL → Step 3: implementasi → Step 4: PASS + suite penuh.**
- [ ] **Step 5: Commit (setelah konfirmasi).** `feat: perhitungan posisi overlay dan matriks dukungan`

---

### Task 5: `HotkeyGesture` + `ConnectivityDebouncer` (X-02, X-03) — kerjakan sebelum Task 2

**Files:**
- Create: `src/NetworkSpeed.Core/Input/HotkeyGesture.cs`, `src/NetworkSpeed.Core/Connectivity/ConnectivityDebouncer.cs`
- Test: `tests/NetworkSpeed.Core.Tests/Input/HotkeyGestureTests.cs`, `Connectivity/ConnectivityDebouncerTests.cs`

**Interfaces:**
- Produces:
  - `[Flags] public enum HotkeyModifiers { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }` (nilai = `MOD_*` Win32)
  - `public sealed record HotkeyGesture(HotkeyModifiers Modifiers, uint VirtualKey) { public static bool TryParse(string? text, out HotkeyGesture? gesture); public override string ToString(); }`. Format `Ctrl+Alt+N`, case-insensitive, minimal satu modifier selain Shift, key A–Z, 0–9, F1–F24.
  - `public enum ConnectivityChange { WentOffline, CameOnline }`
  - `public sealed class ConnectivityDebouncer(TimeSpan stablePeriod) { public ConnectivityChange? Update(bool isOnline, TimeSpan now); }`. Observasi pertama hanya menetapkan state (tanpa event). Perubahan dilaporkan sekali, setelah state baru stabil ≥ `stablePeriod` (default di App: 5 detik).

- [ ] **Step 1: Tulis test yang gagal**: `TryParse_ValidGestures` (Theory: "Ctrl+Alt+N", "ctrl+shift+F12", "Win+Alt+5"), `TryParse_InvalidGestures_ReturnFalse` (Theory: "", "N", "Shift+N", "Ctrl+", "Ctrl+Alt+Foo", "Ctrl+Ctrl+N", null), `ToString_RoundTrips`, `Update_FirstObservation_NoEvent`, `Update_FlapShorterThanStable_NoEvent` (offline 2 s lalu online lagi), `Update_StableOffline_EmitsOnce`, `Update_OfflineThenOnline_EmitsBoth`.
- [ ] **Step 2: FAIL → Step 3: implementasi → Step 4: PASS + suite penuh.**
- [ ] **Step 5: Commit (setelah konfirmasi).** `feat: parsing hotkey dan debounce status koneksi`

---

### Task 6: Interop — pembaca layout taskbar

**Files:**
- Create: `src/NetworkSpeed.Interop/Taskbar/TaskbarLayoutReader.cs`
- Modify: `src/NetworkSpeed.Interop/NativeMethods.txt` (tambah `FindWindowEx`, `GetWindowRect`, `MonitorFromWindow`, `GetMonitorInfo`, `MONITORINFOEXW`)
- Modify: `src/NetworkSpeed.Interop/NetworkSpeed.Interop.csproj` (`<UseWPF>true</UseWPF>` atau referensi `UIAutomationClient`/`UIAutomationTypes`, cukup salah satu yang membuat `System.Windows.Automation` tersedia)
- Test: `tests/NetworkSpeed.Interop.Tests/Taskbar/TaskbarLayoutReaderTests.cs`

**Interfaces:**
- Consumes: `TaskbarLayout`, `PixelRect`, `TaskbarEdge` (Task 4).
- Produces:
  - `public sealed class TaskbarLayoutReader { public IReadOnlyList<TaskbarLayout> ReadAll(); public IReadOnlyList<PixelRect> ReadWin32Rects(); }`
    - `ReadAll`: primary (`Shell_TrayWnd`) + semua `Shell_SecondaryTrayWnd`; Win11 dari anak `TaskbarFrame` (`Children` saja), Win10 dari `ReBarWindow32 > MSTaskSwWClass > MSTaskListWClass`; `TrayLeft` dari `TrayNotifyWnd` (primary). Untuk secondary yang tidak punya tray, pakai `Taskbar.Right`. Exception UIA (`ElementNotAvailableException`, `COMException`, `InvalidOperationException`) → layout dengan `HasUiaDetail=false`, tanpa melempar.
    - `ReadWin32Rects`: murah, hanya `GetWindowRect` taskbar + tray, untuk polling (T-05).
    - Tidak pernah membaca atau mengembalikan `Name`/`AutomationId` tombol (F-07d).

- [ ] **Step 1: Tulis integration test yang gagal** (bergantung lingkungan, tanpa asumsi jumlah app):

```csharp
[Fact] public void ReadAll_HasExactlyOnePrimary()
[Fact] public void ReadAll_TrayLeftInsidePrimaryTaskbar()
[Fact] public void ReadAll_HorizontalPrimary_HasUiaDetail()   // dilewati (Assert.Skip) bila taskbar vertikal
[Fact] public void ReadWin32Rects_MatchesReadAllTaskbarRects()
```

- [ ] **Step 2: FAIL → Step 3: implementasi → Step 4: PASS.**
- [ ] **Step 5: Ukur** waktu `ReadAll()` 20× (Stopwatch di test terpisah yang hanya menulis output, tanpa assert) dan catat di ledger. Target: rata-rata < 50 ms karena query sudah dibatasi ke `Children`.
- [ ] **Step 6: Commit (setelah konfirmasi).** `feat: pembaca layout taskbar Win10/Win11`

---

### Task 7: Interop — shell state, SSID, latency, pemantau perubahan taskbar

**Files:**
- Create: `src/NetworkSpeed.Interop/Shell/ShellState.cs`, `Wlan/WlanSsidReader.cs`, `Net/LatencyProbe.cs`, `Taskbar/TaskbarChangeMonitor.cs`
- Modify: `NativeMethods.txt` (`SHQueryUserNotificationState`, `SHAppBarMessage`, `APPBARDATA`, `WlanOpenHandle`, `WlanCloseHandle`, `WlanQueryInterface`, `WlanFreeMemory`, `WLAN_CONNECTION_ATTRIBUTES`)
- Test: `tests/NetworkSpeed.Interop.Tests/Shell/ShellStateTests.cs`, `Net/LatencyProbeTests.cs`

**Interfaces:**
- Produces:
  - `public static class ShellState { bool IsFullscreenAppActive(); bool IsTaskbarAutoHide(); bool IsLightTheme(); }` (fullscreen = QUNS 2/3/4; tema dari `HKCU\...\Themes\Personalize\SystemUsesLightTheme`)
  - `public sealed class WlanSsidReader { public string? TryReadSsid(Guid interfaceId); }`. Semua kegagalan (tidak ada Wi-Fi, akses ditolak, dan pada Windows 11 24H2+ izin lokasi ditolak) → `null`, tidak melempar.
  - `public sealed class LatencyProbe { public Task<LatencyReading> PingAsync(string? host, TimeSpan timeout, CancellationToken ct); }`. `host == null` → default gateway IPv4 adapter terpilih; tidak ada gateway → `RoundTrip null`. Memakai `System.Net.NetworkInformation.Ping`.
  - `public sealed class TaskbarChangeMonitor : IDisposable { public event Action<string>? Changed; public void Start(); public void Resubscribe(); }`. Subscribe UIA `StructureChanged` (subtree `Shell_TrayWnd`) di thread background; polling Win32 (`ReadWin32Rects`) tiap 2 detik, dan event dipicu hanya bila rect berubah. Reason = `"uia-structure"`/`"win32-rect"`.

- [ ] **Step 1: Tulis test yang gagal**: `ShellState_CallsDoNotThrow` (ketiga method), `LatencyProbe_Loopback_ReturnsRoundTrip` (host "127.0.0.1"), `LatencyProbe_UnroutableHost_TimesOutNotThrows` (host "192.0.2.1", timeout 300 ms → `RoundTrip == null`), `WlanSsidReader_UnknownInterface_ReturnsNull`.
- [ ] **Step 2: FAIL → Step 3: implementasi → Step 4: PASS.**
- [ ] **Step 5: Commit (setelah konfirmasi).** `feat: shell state, SSID, latency, dan pemantau taskbar`

---

### Task 8: Proyek App — kerangka, single instance, autostart gate, tray

**Files:**
- Create: `src/NetworkSpeed.App/NetworkSpeed.App.csproj` (`WinExe`, `UseWPF`, `UseWindowsForms`, `ApplicationManifest`), `app.manifest` (PerMonitorV2, supportedOS Win10), `App.xaml(.cs)`, `Composition/AppHost.cs`, `Platform/SingleInstance.cs`, `Platform/AutostartGate.cs`, `Tray/TrayIconController.cs`, ikon `Assets/app.ico`
- Modify: `NetworkSpeed.slnx` (tambah proyek)
- Test: `tests/NetworkSpeed.Core.Tests/...` tidak ada; logika `AutostartGate` dibuat murni di `src/NetworkSpeed.Core/Startup/AutostartGate.cs` + test

**Interfaces:**
- Produces:
  - `public static class AutostartGate { public static bool ShouldExit(IReadOnlyList<string> args, UserSettings settings); }` → true bila args memuat `--autostart` dan `AutostartEnabled == false`.
  - `SingleInstance`: mutex `Local\NetworkSpeed.App.{SessionId}` dan `EventWaitHandle` `Local\NetworkSpeed.App.ShowSettings.{SessionId}`. Instance kedua memberi sinyal lalu keluar (spec §6).
  - `TrayIconController`: `NotifyIcon` dengan menu Settings, Show/Hide, Exit (History, Per-app usage, dan Check for updates menyusul di Plan 4/5; **jangan** tampilkan item yang belum berfungsi).
  - `AppHost`: membuat `RollingFileLog`, `SettingsFileStore`, `SpeedSampler` (adapter refresh 10 detik + `NetworkChange.NetworkAddressChanged` → `InvalidateAdapters`), `DispatcherTimer` sampling sesuai `RefreshIntervalMs`, dan handler `SystemEvents.PowerModeChanged` (Resume → `OnResume`). Exception tak tertangani dicatat lewat `IAppLog`, tanpa dialog teknis.

- [ ] **Step 1: Tulis test yang gagal** `AutostartGateTests` (Theory 4 kombinasi args × setting).
- [ ] **Step 2: FAIL → Step 3: implementasi** `AutostartGate` + kerangka App.
- [ ] **Step 4: Verifikasi.** `dotnet build -c Release -warnaserror` 0 warning; `dotnet test` lolos; jalankan `dotnet run --project src/NetworkSpeed.App` → tray icon muncul; jalankan lagi → instance kedua keluar dan instance pertama menerima sinyal (catat di log `ShowSettings signal`); menu Exit menutup app; `settings.json` dibuat di `%LOCALAPPDATA%\NetworkSpeed`.
- [ ] **Step 5: Perbarui CLAUDE.md** bagian "Menjalankan aplikasi" (hapus catatan "dibuat di Plan 2").
- [ ] **Step 6: Commit (setelah konfirmasi).** `feat: kerangka aplikasi, single instance, dan tray icon`

---

### Task 9: Overlay window + coordinator (F-07, T-05, T-09, T-10, D-01, D-05, E-01, E-03, E-10)

**Files:**
- Create: `src/NetworkSpeed.App/Overlay/OverlayWindow.xaml(.cs)`, `Overlay/OverlayCoordinator.cs`

**Interfaces:**
- Consumes: `TaskbarLayoutReader`, `TaskbarChangeMonitor`, `PlacementCalculator`, `MonitorSelector`, `OverlayTextBuilder`, `ShellState`, `UserSettings`.
- Produces:
  - `OverlayWindow`: borderless, `AllowsTransparency`, latar `#01000000` (agar bisa diklik kanan), `ShowInTaskbar=false`, `ShowActivated=false`, ex-style `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST`. `void ShowText(OverlayText text, Brush up, Brush down, Brush alert)`; `void PlaceAt(int x, int y)` via `SetWindowPos(HWND_TOPMOST, …, SWP_NOSIZE|SWP_NOACTIVATE|SWP_SHOWWINDOW)`; `void HideOverlay()`. Context menu = menu yang sama dengan tray.
  - `OverlayCoordinator`: satu `OverlayWindow` per layout hasil `MonitorSelector`. Recalc (debounce 250 ms) dipicu oleh: `TaskbarChangeMonitor.Changed`, `TaskbarCreated` (lalu `Resubscribe()`), `WM_SETTINGCHANGE`, `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, resume, dan perubahan settings. `ReadAll()` dijalankan di `Task.Run`, satu recalc pada satu waktu. Penyembunyian: fullscreen (bila `HideOnFullscreen`), auto-hide (taskbar keluar layar, polling 200 ms hanya saat auto-hide aktif), atau `Placement.IsVisible == false`. Reason penyembunyian disimpan untuk ditampilkan di Settings. Warna: tema dari `ShellState.IsLightTheme()` + override `Theme`; mode kontras tinggi (`SystemParameters.HighContrast`) → `SystemColors`.

- [ ] **Step 1: Verifikasi manual di PC pengembang** (tidak ada test otomatis untuk jendela):
  - Right/Center/Left tampil di posisi yang sama dengan hasil spike (lihat log `LOCATE`, atau tooltip debug saat `--diagnostics`).
  - Buka/tutup app → Center bergeser.
  - `taskkill /f /im explorer.exe` lalu `start explorer` → overlay kembali ≤ 1 detik.
  - Video fullscreen → overlay hilang, keluar fullscreen → muncul.
  - Klik taskbar → overlay tetap di atas.
- [ ] **Step 2: Ukur CPU & RAM** (Release, 10 menit idle): `Get-Process NetworkSpeed.App` CPU delta dan `PrivateMemorySize64`. Catat di ledger. Target CPU < 1% total. **RAM > 60 MB → laporkan ke pemilik produk (keputusan terbuka dari go/no-go), jangan ubah arsitektur sendiri.**
- [ ] **Step 3: `dotnet build -warnaserror` + `dotnet test` + `dotnet format --verify-no-changes`.**
- [ ] **Step 4: Commit (setelah konfirmasi).** `feat: overlay di taskbar dengan posisi Left/Center/Right`

---

### Task 10: Jendela Settings (F-02…F-05, F-07a, T-06, D-01…D-04, E-03, X-01…X-03, §6)

**Files:**
- Create: `src/NetworkSpeed.App/Settings/SettingsWindow.xaml(.cs)`, `Settings/SettingsViewModel.cs`
- Create: `src/NetworkSpeed.Core/Settings/SettingsEditor.cs` + test (logika murni: apply perubahan, validasi via `SettingsValidator`, daftar posisi dengan status dari `PositionSupportEvaluator`)

**Interfaces:**
- Produces:
  - `public static class SettingsEditor { public static IReadOnlyList<PositionOption> PositionOptions(IReadOnlyList<TaskbarLayout> layouts); }` dengan `public sealed record PositionOption(OverlayPosition Position, bool IsEnabled, string? DisabledReason);`. Posisi didukung bila didukung di **semua** taskbar yang ditampilkan; reason dari taskbar pertama yang menolak.
  - Settings window: grup General (unit, refresh, autostart), Adapter (mode, adapter tertentu dari `ReadAdapters`, VPN), Position (Left/Center/Right dengan status + offset X/Y per posisi), Appearance (layout, font, ukuran, warna, transparansi, tema, ambang & warna alert, hide on fullscreen), Monitors, Latency (aktif, host, interval, tampil di overlay, nonaktif + alasan di TwoLines), Notifications, Hotkey (field + status "registered"/"in use by another app"). Perubahan diterapkan langsung (live) dan disimpan saat jendela ditutup; tombol "Reset to defaults". Semua kontrol punya `AutomationProperties.Name` (D-06).

- [ ] **Step 1: Tulis test yang gagal** `PositionOptions_Win11Centered_AllEnabled`, `PositionOptions_MixedTaskbars_LeftDisabledWithReason`.
- [ ] **Step 2: FAIL → Step 3: implementasi** editor + window → **Step 4: PASS + suite penuh.**
- [ ] **Step 5: Verifikasi manual**: setiap kontrol mengubah overlay secara live; tutup → `settings.json` berisi nilai baru; Left nonaktif + alasan saat taskbar rata kiri; navigasi keyboard penuh dan Narrator membaca label.
- [ ] **Step 6: Commit (setelah konfirmasi).** `feat: jendela Settings`

---

### Task 11: Tooltip, ping, hotkey, notifikasi koneksi (F-06, X-01, X-02, X-03)

**Files:**
- Create: `src/NetworkSpeed.App/Platform/GlobalHotkey.cs`, `Notifications/BalloonNotifier.cs`
- Create: `src/NetworkSpeed.Core/Display/TooltipBuilder.cs` + test
- Modify: `Overlay/OverlayCoordinator.cs`, `Composition/AppHost.cs`

**Interfaces:**
- Produces:
  - `public sealed record TooltipData(string? AdapterName, string? Ssid, string? Ipv4, ulong SessionSentBytes, ulong SessionReceivedBytes, LatencyReading? Latency, string? HiddenReason);`
  - `public static class TooltipBuilder { public static string Build(TooltipData d, SpeedUnitMode mode); }`. Baris kosong untuk nilai null tidak ditampilkan; total sesi diformat dengan satuan byte (KB/MB/GB, skala 1024).
  - `GlobalHotkey`: `RegisterHotKey` pada HWND tersembunyi; gagal → `IsRegistered=false` + log warn, tanpa exception.
  - `BalloonNotifier`: `NotifyIcon.ShowBalloonTip` untuk "Connection lost" / "Connection restored" dari `ConnectivityDebouncer` (stable 5 detik), hanya bila `ConnectivityNotifications`.
  - Ping berjalan di timer terpisah (`PingIntervalMs`, timeout 1 detik), tidak di thread UI.

- [ ] **Step 1: Tulis test yang gagal** `TooltipBuilder_AllFields`, `TooltipBuilder_NullFieldsOmitted`, `TooltipBuilder_HiddenReasonShown`.
- [ ] **Step 2: FAIL → Step 3: implementasi → Step 4: PASS + suite penuh.**
- [ ] **Step 5: Verifikasi manual**: hover overlay → tooltip lengkap (SSID kosong bila izin lokasi ditolak, tanpa error); `Ctrl+Alt+N` sembunyi/tampil; matikan Wi-Fi 10 detik → balloon "Connection lost", nyalakan → "Connection restored"; Wi-Fi mati-nyala 2 detik → tidak ada balloon; set hotkey yang dipakai app lain → Settings menampilkan "in use".
- [ ] **Step 6: Commit (setelah konfirmasi).** `feat: tooltip, ping, hotkey, dan notifikasi koneksi`

---

### Task 12: Dokumentasi pengguna & pengembang

**Files:**
- Create/Modify: `README.md` (cara build, jalankan, lokasi settings & log, matriks posisi F-07c, batasan yang diketahui: taskbar vertikal, SSID butuh izin lokasi di Win11 24H2+)
- Modify: `CLAUDE.md` bila perintah berubah

- [ ] **Step 1: Tulis README** sesuai daftar di atas.
- [ ] **Step 2: Commit (setelah konfirmasi).** `docs: README aplikasi overlay`

---

### Task 13: Uji penerimaan (checklist spike §6) & go untuk Plan 3/4

**Files:**
- Modify: `docs/superpowers/spikes/2026-11-12-taskbar-locator-results.md` (§5 matriks & §6 checklist diisi dengan hasil App produksi)
- Modify: spec F-07c (⏳ → ✅/❌)

- [ ] **Step 1: Jalankan checklist §6 di Win11** (PC pengembang): skenario 1, 2, 5–15.
- [ ] **Step 2: Pengguna menjalankan checklist di Windows 10** (skenario 3–15) dengan build self-contained (`dotnet publish src/NetworkSpeed.App -c Release -r win-x64 --self-contained -o out/app`).
- [ ] **Step 3: Perbarui matriks F-07c** sesuai hasil; posisi yang gagal di suatu kombinasi ditandai ❌ beserta alasannya di `PositionSupportEvaluator` (test baru untuk kombinasi itu, RED → GREEN).
- [ ] **Step 4: Laporkan CPU/RAM final** ke pemilik produk bersama keputusan RAM (bila > 60 MB).
- [ ] **Step 5: Commit (setelah konfirmasi).** `docs: hasil uji penerimaan overlay`
