# Plan 1 — Fondasi & Spike Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Menyiapkan solution .NET, membangun logika inti pengukuran kecepatan yang teruji (dipakai App dan Service), lalu menjalankan dua spike berisiko (posisi taskbar & ETW per aplikasi) sampai keputusan go/no-go.

**Architecture:** `NetworkSpeed.Core` (net10.0, tanpa dependensi Windows) memuat logika murni: perhitungan kecepatan, format satuan, klasifikasi & pemilihan adapter. `NetworkSpeed.Interop` (net10.0-windows) membaca counter dari `GetIfTable2`. Dua spike di `spikes/` bersifat **throwaway**: hasilnya adalah dokumen temuan, bukan kode produksi.

**Tech Stack:** C# / .NET 10 LTS, xUnit v3, Microsoft.Windows.CsWin32 (P/Invoke generator), Microsoft.Diagnostics.Tracing.TraceEvent (spike ETW saja), WPF (spike taskbar saja).

**Spec:** `docs/superpowers/specs/2026-11-11-network-speed-design.md`

## Peta rencana (roadmap)

Spec mencakup beberapa subsistem, jadi dipecah menjadi beberapa plan. Plan berikutnya **ditulis setelah go/no-go Plan 1**, karena hasil spike mengubah isinya (matriks F-07a, akun service S-03).

| Plan | Isi | Bergantung pada |
|------|-----|-----------------|
| **1 (dokumen ini)** | Solution, Core (F-01/F-02/F-04/F-05), Interop `GetIfTable2`, spike taskbar, spike ETW, go/no-go | — |
| 2 | App WPF: overlay, taskbar locator produksi (F-07, T-05…T-10), tray, Settings, tampilan (D-01…D-06), X-01…X-03, E-01/E-03/E-10 | Plan 1 + go/no-go |
| 3 | Service: history sampler, SQLite (H-05/H-06/E-15), named pipe (S-04…S-08, H-07), ETW per aplikasi (S-01…S-03, S-09) | Plan 1 + go/no-go |
| 4 | Integrasi App ↔ Service: popup riwayat (H-02), kuota & notifikasi (H-01), per aplikasi UI | Plan 2 + 3 |
| 5 | Installer silent, code signing self-signed, cek update shared folder (§8–§10), README untuk IT | Plan 4 |

## Global Constraints

- Target framework: `net10.0` untuk `NetworkSpeed.Core`; `net10.0-windows10.0.19041.0` untuk proyek yang memakai Win32/WPF. Platform `x64`.
- OS yang harus didukung: Windows 10 22H2 dan Windows 11 23H2+ (spec C-01).
- `Nullable` enable, `TreatWarningsAsErrors` true, `AnalysisLevel` latest-recommended untuk semua proyek non-spike.
- Teks yang tampil ke pengguna dalam **bahasa Inggris**, format angka **selalu `CultureInfo.InvariantCulture`** (spec C-07).
- Skala satuan: **1000 untuk bit/s, 1024 untuk byte/s** (spec F-02).
- Waktu untuk perhitungan kecepatan berasal dari **monotonic clock** (`Stopwatch`), tidak pernah `DateTime.Now` (spec F-01, E-14).
- Delta counter negatif = 0; sampel pertama setelah baseline baru tidak menghasilkan kecepatan (spec E-05/E-07).
- Adapter diidentifikasi dengan `InterfaceGuid` (`Guid`), tidak dengan nama (spec F-04).
- Tidak ada injection ke `explorer.exe`, hook global, atau DeskBand, termasuk di spike (spec C-03).
- Tidak mencatat IP, SSID, host ping, atau nama proses ke log (spec §7). Spike boleh menampilkan data ke konsol lokal, tetapi **dokumen hasil spike tidak memuat** IP/SSID/nama host.
- Paket NuGet diatur terpusat di `Directory.Packages.props` dengan versi stabil terbaru saat Task 0 disetujui; versi dicatat di file itu, bukan di tiap `.csproj`.
- Setiap langkah **Commit** butuh konfirmasi eksplisit pengguna dulu (CLAUDE.md). Format pesan `tipe: deskripsi singkat`, **tanpa atribusi AI**.

## Review Focus

1. **Adapter dicabut/dicolok di tengah sampling** → interface yang hilang dibuang dari hasil, interface baru baru dihitung di sampel berikutnya, tidak ada lonjakan. Dipin di Task 2 (`AddSnapshot_InterfaceAppearsLater_NoRateUntilSecondSample`, `AddSnapshot_InterfaceDisappears_IsDropped`).
2. **Jeda waktu tidak wajar** (elapsed 0 karena dua pembacaan sangat cepat, atau elapsed sangat besar karena timer tertahan) → elapsed ≤ 0 tidak menghasilkan kecepatan; elapsed besar menghasilkan rata-rata, bukan angka yang digelembungkan. Dipin di Task 2 (`AddSnapshot_ZeroElapsed_ReturnsNull`, `AddSnapshot_LongElapsed_AveragesOverElapsed`).
3. **Pembulatan di batas satuan** (mis. 999,96 KB/s, 1023,99 KB/s) → harus naik ke satuan berikutnya, tidak pernah tampil "1000 KB/s" atau "1024 KB/s". Dipin di Task 3.
4. **Locale Windows Indonesia** (desimal koma) → tampilan tetap `1.2 MB/s`. Dipin di Task 3 (`Format_UnderIndonesianCulture_UsesDot`).
5. **Adapter pilihan pengguna tidak ada lagi / di-rename** → pemilihan berdasarkan Guid tetap benar setelah rename; adapter yang hilang menghasilkan set kosong (App menampilkan "Offline"), bukan exception. Dipin di Task 4.

---

## Struktur file

```
NetworkSpeed.sln
global.json                       # SDK 10.0.x, rollForward latestFeature
Directory.Build.props             # TFM, Nullable, TreatWarningsAsErrors, Platform x64
Directory.Packages.props          # Central Package Management
.editorconfig
.gitignore                        # bin/, obj/, *.pfx, *.user, .vs/
src/
  NetworkSpeed.Core/
    Time/IMonotonicClock.cs
    Time/StopwatchClock.cs
    Sampling/InterfaceCounterSnapshot.cs
    Sampling/InterfaceRate.cs
    Sampling/SpeedCalculator.cs
    Formatting/SpeedUnitMode.cs
    Formatting/SpeedFormatter.cs
    Adapters/AdapterKind.cs
    Adapters/AdapterInfo.cs
    Adapters/AdapterClassifier.cs
    Adapters/AdapterSelectionMode.cs
    Adapters/AdapterSelector.cs
  NetworkSpeed.Interop/
    NativeMethods.txt             # daftar API untuk CsWin32
    IInterfaceCounterSource.cs
    IfTableCounterSource.cs
tests/
  NetworkSpeed.Core.Tests/
    Sampling/SpeedCalculatorTests.cs
    Formatting/SpeedFormatterTests.cs
    Adapters/AdapterClassifierTests.cs
    Adapters/AdapterSelectorTests.cs
  NetworkSpeed.Interop.Tests/
    IfTableCounterSourceTests.cs
tools/
  SpeedProbe/                     # konsol: cetak kecepatan per detik, untuk dibandingkan dengan Task Manager
spikes/                           # THROWAWAY, tidak masuk solution utama
  TaskbarLocatorSpike/
  EtwSpike/
docs/superpowers/spikes/
  2026-11-xx-speed-accuracy.md
  2026-11-xx-taskbar-locator-results.md
  2026-11-xx-etw-results.md
```

---

### Task 0: Prasyarat (butuh tindakan & persetujuan pengguna)

**Files:** tidak ada.

- [ ] **Step 1: Pengguna memasang .NET 10 SDK (x64).** Saat plan ini ditulis, `dotnet` belum terpasang di mesin. Unduh dari situs resmi Microsoft (dotnet.microsoft.com).
  Verifikasi: `dotnet --list-sdks` → menampilkan baris `10.0.x`.
- [ ] **Step 2: Minta persetujuan pengguna untuk `git init`** di folder proyek (folder belum berupa repo git). Setelah disetujui: `git init` lalu `git branch -M main`.
- [ ] **Step 3: Minta persetujuan pengguna untuk paket berikut** (CLAUDE.md: menambah dependensi wajib dikonfirmasi): `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Microsoft.Windows.CsWin32`; khusus spike: `Microsoft.Diagnostics.Tracing.TraceEvent`. Catat versi stabil terbaru yang disetujui.

---

### Task 1: Scaffold solution

**Files:**
- Create: `NetworkSpeed.sln`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`
- Create: `src/NetworkSpeed.Core/NetworkSpeed.Core.csproj`, `src/NetworkSpeed.Interop/NetworkSpeed.Interop.csproj`
- Create: `tests/NetworkSpeed.Core.Tests/NetworkSpeed.Core.Tests.csproj`, `tests/NetworkSpeed.Interop.Tests/NetworkSpeed.Interop.Tests.csproj`

**Interfaces:**
- Produces: solution yang bisa di-build dan di-test dengan perintah di CLAUDE.md.

- [ ] **Step 1: Buat file-file di atas.** `Directory.Build.props` menetapkan nilai dari Global Constraints. `NetworkSpeed.Interop` mereferensikan `NetworkSpeed.Core`; masing-masing proyek test mereferensikan proyek yang diuji. `.gitignore` wajib memuat `*.pfx`.
- [ ] **Step 2: Tambahkan satu test placeholder** `ScaffoldTests.Solution_Builds()` yang `Assert.True(true)` di `NetworkSpeed.Core.Tests` (dihapus di Task 2).
- [ ] **Step 3: Verifikasi.**
  Run: `dotnet build -c Release -warnaserror` → `Build succeeded. 0 Warning(s) 0 Error(s)`
  Run: `dotnet test` → `Passed! - Failed: 0, Passed: 1`
  Run: `dotnet format --verify-no-changes` → exit code 0
- [ ] **Step 4: Perbarui CLAUDE.md** bagian Perintah bila path aktual berbeda dari yang tertulis (mis. path `dotnet run`).
- [ ] **Step 5: Commit (setelah konfirmasi pengguna).**

```bash
git add .
git commit -m "chore: scaffold solution network speed"
```

---

### Task 2: SpeedCalculator (F-01, E-05, E-07, E-14)

**Files:**
- Create: `src/NetworkSpeed.Core/Time/IMonotonicClock.cs`, `src/NetworkSpeed.Core/Time/StopwatchClock.cs`
- Create: `src/NetworkSpeed.Core/Sampling/InterfaceCounterSnapshot.cs`, `src/NetworkSpeed.Core/Sampling/InterfaceRate.cs`, `src/NetworkSpeed.Core/Sampling/SpeedCalculator.cs`
- Test: `tests/NetworkSpeed.Core.Tests/Sampling/SpeedCalculatorTests.cs`
- Delete: test placeholder dari Task 1

**Interfaces:**
- Produces:
  - `public interface IMonotonicClock { TimeSpan Elapsed { get; } }`
  - `public sealed class StopwatchClock : IMonotonicClock` (membungkus `Stopwatch.GetTimestamp()`)
  - `public readonly record struct InterfaceCounterSnapshot(Guid InterfaceId, ulong BytesReceived, ulong BytesSent);`
  - `public readonly record struct InterfaceRate(Guid InterfaceId, double ReceivedBytesPerSecond, double SentBytesPerSecond);`
  - `public sealed class SpeedCalculator`
    - `public IReadOnlyDictionary<Guid, InterfaceRate>? AddSnapshot(IReadOnlyList<InterfaceCounterSnapshot> snapshots, TimeSpan timestamp)`. Mengembalikan `null` bila belum ada baseline atau elapsed ≤ 0.
    - `public void ResetBaseline()`, dipanggil saat resume dari sleep.

- [ ] **Step 1: Tulis test yang gagal.**

```csharp
public class SpeedCalculatorTests
{
    static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    static InterfaceCounterSnapshot S(Guid id, ulong rx, ulong tx) => new(id, rx, tx);
    static TimeSpan Sec(double s) => TimeSpan.FromSeconds(s);

    [Fact] public void AddSnapshot_FirstSample_ReturnsNull()
    {
        var calc = new SpeedCalculator();
        Assert.Null(calc.AddSnapshot([S(A, 100, 50)], Sec(0)));
    }

    [Fact] public void AddSnapshot_SecondSample_ComputesBytesPerSecond()
    {
        var calc = new SpeedCalculator();
        calc.AddSnapshot([S(A, 1_000, 500)], Sec(10));
        var r = calc.AddSnapshot([S(A, 3_000, 1_500)], Sec(12))!;
        Assert.Equal(1_000d, r[A].ReceivedBytesPerSecond);
        Assert.Equal(500d, r[A].SentBytesPerSecond);
    }

    [Fact] public void AddSnapshot_CounterDecreased_RateIsZero()
    {
        var calc = new SpeedCalculator();
        calc.AddSnapshot([S(A, 5_000, 5_000)], Sec(0));
        var r = calc.AddSnapshot([S(A, 100, 6_000)], Sec(1))!;
        Assert.Equal(0d, r[A].ReceivedBytesPerSecond);
        Assert.Equal(1_000d, r[A].SentBytesPerSecond);
    }

    [Fact] public void AddSnapshot_ZeroElapsed_ReturnsNull()
    {
        var calc = new SpeedCalculator();
        calc.AddSnapshot([S(A, 0, 0)], Sec(5));
        Assert.Null(calc.AddSnapshot([S(A, 999, 999)], Sec(5)));
    }

    [Fact] public void AddSnapshot_LongElapsed_AveragesOverElapsed()
    {
        var calc = new SpeedCalculator();
        calc.AddSnapshot([S(A, 0, 0)], Sec(0));
        var r = calc.AddSnapshot([S(A, 60_000, 0)], Sec(60))!;
        Assert.Equal(1_000d, r[A].ReceivedBytesPerSecond);
    }

    [Fact] public void AddSnapshot_InterfaceAppearsLater_NoRateUntilSecondSample()
    {
        var calc = new SpeedCalculator();
        calc.AddSnapshot([S(A, 0, 0)], Sec(0));
        var r1 = calc.AddSnapshot([S(A, 100, 0), S(B, 9_999_999, 0)], Sec(1))!;
        Assert.False(r1.ContainsKey(B));
        var r2 = calc.AddSnapshot([S(A, 200, 0), S(B, 10_000_999, 0)], Sec(2))!;
        Assert.Equal(1_000d, r2[B].ReceivedBytesPerSecond);
    }

    [Fact] public void AddSnapshot_InterfaceDisappears_IsDropped()
    {
        var calc = new SpeedCalculator();
        calc.AddSnapshot([S(A, 0, 0), S(B, 0, 0)], Sec(0));
        var r = calc.AddSnapshot([S(A, 100, 0)], Sec(1))!;
        Assert.False(r.ContainsKey(B));
        Assert.True(r.ContainsKey(A));
    }

    [Fact] public void ResetBaseline_NextSampleReturnsNull()
    {
        var calc = new SpeedCalculator();
        calc.AddSnapshot([S(A, 0, 0)], Sec(0));
        calc.ResetBaseline();
        Assert.Null(calc.AddSnapshot([S(A, 1_000_000, 0)], Sec(1)));
    }

    [Fact] public void AddSnapshot_EmptyList_ReturnsEmptyAfterBaseline()
    {
        var calc = new SpeedCalculator();
        calc.AddSnapshot([], Sec(0));
        Assert.Empty(calc.AddSnapshot([], Sec(1))!);
    }
}
```

- [ ] **Step 2: Jalankan test, pastikan gagal.**
  Run: `dotnet test --filter "FullyQualifiedName~SpeedCalculatorTests"` → FAIL (compile error: `SpeedCalculator` tidak ada).
- [ ] **Step 3: Implementasikan tipe-tipe di Interfaces.** Baseline disimpan di `Dictionary<Guid, InterfaceCounterSnapshot>` + timestamp terakhir; setiap panggilan mengganti seluruh baseline dengan snapshot terbaru, termasuk saat elapsed ≤ 0. Pengurangan `ulong` dicek dulu (`current >= previous`) supaya tidak terjadi underflow.
- [ ] **Step 4: Jalankan test, pastikan lolos.**
  Run: `dotnet test --filter "FullyQualifiedName~SpeedCalculatorTests"` → `Passed: 9`
- [ ] **Step 5: Commit (setelah konfirmasi).** `feat: tambah SpeedCalculator berbasis monotonic clock`

---

### Task 3: SpeedFormatter (F-02, Review Focus 3–4)

**Files:**
- Create: `src/NetworkSpeed.Core/Formatting/SpeedUnitMode.cs`, `src/NetworkSpeed.Core/Formatting/SpeedFormatter.cs`
- Test: `tests/NetworkSpeed.Core.Tests/Formatting/SpeedFormatterTests.cs`

**Interfaces:**
- Produces:
  - `public enum SpeedUnitMode { BytesPerSecond, BitsPerSecond }`
  - `public static class SpeedFormatter { public static string Format(double bytesPerSecond, SpeedUnitMode mode); }`

**Aturan format** (keputusan plan, memenuhi "lebar stabil" di F-02):
- Byte: satuan `KB/s`, `MB/s`, `GB/s`, `TB/s`, skala 1024. Bit: `Kbps`, `Mbps`, `Gbps`, `Tbps`, skala 1000 (bit = byte × 8).
- Satuan terkecil adalah K (nilai 0 → `0.0 KB/s` / `0.0 Kbps`).
- Pembulatan selalu `Math.Round(v, d, MidpointRounding.AwayFromZero)` (bukan banker's rounding), lalu format dengan jumlah desimal `d` yang sama.
- Jumlah desimal ditentukan dari nilai **setelah dibulatkan ke 1 desimal**: < 10 → 1 desimal; ≥ 10 → tanpa desimal (jadi 9,96 tampil `10`, bukan `10.0`). Kalau hasil akhir pembulatan ≥ 1000, naik satu satuan dan ulangi aturan ini.
- Nilai negatif, `NaN`, atau `Infinity` → diperlakukan sebagai 0.

- [ ] **Step 1: Tulis test yang gagal.**

```csharp
public class SpeedFormatterTests
{
    [Theory]
    [InlineData(0d, "0.0 KB/s")]
    [InlineData(512d, "0.5 KB/s")]
    [InlineData(1024d * 9.94, "9.9 KB/s")]
    [InlineData(1024d * 9.96, "10 KB/s")]
    [InlineData(1024d * 512, "512 KB/s")]
    [InlineData(1024d * 999.6, "1.0 MB/s")]
    [InlineData(1024d * 1023.99, "1.0 MB/s")]
    [InlineData(1024d * 1024 * 5.43, "5.4 MB/s")]
    [InlineData(1024d * 1024 * 1024 * 1.25, "1.3 GB/s")]
    public void Format_Bytes(double bps, string expected)
        => Assert.Equal(expected, SpeedFormatter.Format(bps, SpeedUnitMode.BytesPerSecond));

    [Theory]
    [InlineData(0d, "0.0 Kbps")]
    [InlineData(125d, "1.0 Kbps")]
    [InlineData(124_995d, "1.0 Mbps")]     // 999.96 Kbps → naik satuan
    [InlineData(1_250_000d, "10 Mbps")]
    [InlineData(125_000_000d * 10, "10 Gbps")]
    public void Format_Bits(double bytesPerSecond, string expected)
        => Assert.Equal(expected, SpeedFormatter.Format(bytesPerSecond, SpeedUnitMode.BitsPerSecond));

    [Theory]
    [InlineData(-5d)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void Format_InvalidInput_TreatedAsZero(double v)
        => Assert.Equal("0.0 KB/s", SpeedFormatter.Format(v, SpeedUnitMode.BytesPerSecond));

    [Fact]
    public void Format_UnderIndonesianCulture_UsesDot()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("id-ID");
            Assert.Equal("5.4 MB/s", SpeedFormatter.Format(1024d * 1024 * 5.43, SpeedUnitMode.BytesPerSecond));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
}
```

- [ ] **Step 2: Jalankan → FAIL** (`SpeedFormatter` tidak ada).
  Run: `dotnet test --filter "FullyQualifiedName~SpeedFormatterTests"`
- [ ] **Step 3: Implementasikan `SpeedFormatter.Format`** sesuai aturan di atas, memakai `CultureInfo.InvariantCulture`. Satuan disimpan sebagai konstanta bernama, bukan string ajaib di tengah logika.
- [ ] **Step 4: Jalankan → PASS** (semua kasus).
- [ ] **Step 5: Commit (setelah konfirmasi).** `feat: tambah SpeedFormatter dengan lebar stabil`

---

### Task 4: Klasifikasi & pemilihan adapter (F-04, F-05, Review Focus 5)

**Files:**
- Create: `src/NetworkSpeed.Core/Adapters/AdapterKind.cs`, `AdapterInfo.cs`, `AdapterClassifier.cs`, `AdapterSelectionMode.cs`, `AdapterSelector.cs`
- Test: `tests/NetworkSpeed.Core.Tests/Adapters/AdapterClassifierTests.cs`, `AdapterSelectorTests.cs`

**Interfaces:**
- Produces:
  - `public enum AdapterKind { Ethernet, Wireless, Vpn, Virtual, Loopback, Other }`
  - `public sealed record AdapterInfo(Guid Id, string Name, AdapterKind Kind, bool IsUp, bool HasDefaultGateway);`
  - `public static class AdapterClassifier { public static AdapterKind? Classify(uint ifType, bool isHardwareInterface, bool isFilterInterface); }`. Mengembalikan `null` untuk interface filter (diabaikan seluruhnya).
  - `public enum AdapterSelectionMode { Auto, All, Specific }`
  - `public static class AdapterSelector { public static IReadOnlySet<Guid> Select(IReadOnlyCollection<AdapterInfo> adapters, AdapterSelectionMode mode, Guid? specificId, bool includeVpnInAll); }`

**Aturan klasifikasi** (nilai `IfType` dari IANA ifType, `ipifcons.h`):

| Kondisi | Hasil |
|---|---|
| `isFilterInterface` | `null` |
| `ifType == 24` (softwareLoopback) | `Loopback` |
| `ifType == 71` (ieee80211) | `Wireless` |
| `ifType == 6` (ethernetCsmacd) && hardware | `Ethernet` |
| `ifType ∈ {23 (ppp), 131 (tunnel), 53 (propVirtual)}` && !hardware | `Vpn` |
| `ifType == 6` && !hardware | `Virtual` (Hyper-V/WSL vEthernet) |
| lainnya | `Other` |

Catatan: VPN berbasis adapter Ethernet virtual (mis. TAP) akan terbaca `Virtual`, jadi ikut dikecualikan dari All secara default. Ini sejalan dengan tujuan F-05. Ketepatan klasifikasi dicek di Task 5 Step 5 di PC kantor.

**Aturan pemilihan:**
- `Auto`: adapter `IsUp && HasDefaultGateway` dengan Kind ∈ {Ethernet, Wireless, Other}; kalau kosong, pakai yang Kind = Vpn dengan syarat yang sama.
- `All`: adapter `IsUp` dengan Kind ∈ {Ethernet, Wireless, Other}, ditambah Vpn bila `includeVpnInAll`.
- `Specific`: `{specificId}` bila adapter itu ada dan `IsUp`; selain itu set kosong. `specificId == null` → set kosong.
- Loopback dan Virtual tidak pernah dipilih oleh Auto/All.

- [ ] **Step 1: Tulis test yang gagal.** Satu `[Theory]` untuk setiap baris tabel klasifikasi, ditambah test pemilihan berikut:

```csharp
public class AdapterSelectorTests
{
    static AdapterInfo Ad(int n, AdapterKind k, bool up = true, bool gw = true, string? name = null)
        => new(new Guid(n, 0, 0, new byte[8]), name ?? $"nic{n}", k, up, gw);

    [Fact] public void Auto_PicksUpPhysicalWithGateway_IgnoresVpnWhenPhysicalExists()
    {
        var list = new[] { Ad(1, AdapterKind.Ethernet), Ad(2, AdapterKind.Vpn), Ad(3, AdapterKind.Wireless, gw: false) };
        Assert.Equal(new[] { list[0].Id }, AdapterSelector.Select(list, AdapterSelectionMode.Auto, null, false));
    }

    [Fact] public void Auto_FallsBackToVpn_WhenNoPhysicalHasGateway()
    {
        var list = new[] { Ad(1, AdapterKind.Ethernet, gw: false), Ad(2, AdapterKind.Vpn) };
        Assert.Equal(new[] { list[1].Id }, AdapterSelector.Select(list, AdapterSelectionMode.Auto, null, false));
    }

    [Fact] public void All_ExcludesLoopbackVirtualAndDown_AndVpnByDefault()
    {
        var list = new[] { Ad(1, AdapterKind.Ethernet), Ad(2, AdapterKind.Loopback), Ad(3, AdapterKind.Virtual),
                           Ad(4, AdapterKind.Wireless, up: false), Ad(5, AdapterKind.Vpn) };
        Assert.Equal(new[] { list[0].Id }, AdapterSelector.Select(list, AdapterSelectionMode.All, null, false));
        Assert.Equal(new[] { list[0].Id, list[4].Id }.ToHashSet(),
                     AdapterSelector.Select(list, AdapterSelectionMode.All, null, true).ToHashSet());
    }

    [Fact] public void Specific_SurvivesRename()
    {
        var renamed = Ad(7, AdapterKind.Ethernet, name: "Renamed NIC");
        Assert.Equal(new[] { renamed.Id }, AdapterSelector.Select([renamed], AdapterSelectionMode.Specific, renamed.Id, false));
    }

    [Fact] public void Specific_MissingOrDown_ReturnsEmpty()
    {
        var down = Ad(1, AdapterKind.Ethernet, up: false);
        Assert.Empty(AdapterSelector.Select([down], AdapterSelectionMode.Specific, down.Id, false));
        Assert.Empty(AdapterSelector.Select([down], AdapterSelectionMode.Specific, Guid.NewGuid(), false));
        Assert.Empty(AdapterSelector.Select([down], AdapterSelectionMode.Specific, null, false));
    }

    [Fact] public void AnyMode_EmptyList_ReturnsEmpty()
    {
        foreach (var m in Enum.GetValues<AdapterSelectionMode>())
            Assert.Empty(AdapterSelector.Select([], m, null, true));
    }
}
```

- [ ] **Step 2: Jalankan → FAIL.**
  Run: `dotnet test --filter "FullyQualifiedName~Adapter"`
- [ ] **Step 3: Implementasikan `AdapterClassifier.Classify` dan `AdapterSelector.Select`** sesuai tabel. Nilai `ifType` sebagai konstanta bernama (`IfTypeEthernetCsmacd = 6`, dst.).
- [ ] **Step 4: Jalankan → PASS.**
- [ ] **Step 5: Commit (setelah konfirmasi).** `feat: tambah klasifikasi dan pemilihan adapter`

---

### Task 5: Interop `GetIfTable2` + SpeedProbe

**Files:**
- Create: `src/NetworkSpeed.Interop/NativeMethods.txt` (isi: `GetIfTable2`, `FreeMibTable`, `MIB_IF_TABLE2`, `MIB_IF_ROW2`)
- Create: `docs/superpowers/spikes/2026-11-xx-speed-accuracy.md`
- Create: `src/NetworkSpeed.Interop/IInterfaceCounterSource.cs`, `IfTableCounterSource.cs`
- Create: `tools/SpeedProbe/SpeedProbe.csproj`, `tools/SpeedProbe/Program.cs`
- Test: `tests/NetworkSpeed.Interop.Tests/IfTableCounterSourceTests.cs`

**Interfaces:**
- Consumes: `InterfaceCounterSnapshot`, `AdapterInfo`, `AdapterClassifier` (Task 2, 4).
- Produces:
  - `public interface IInterfaceCounterSource { IReadOnlyList<InterfaceCounterSnapshot> ReadCounters(); IReadOnlyList<AdapterInfo> ReadAdapters(); }`
  - `public sealed class IfTableCounterSource : IInterfaceCounterSource`

**Pendekatan:**
- `ReadCounters`: satu panggilan `GetIfTable2`, ambil `InterfaceGuid`, `InOctets`, `OutOctets` untuk baris yang lolos `AdapterClassifier` (bukan filter). Selalu `FreeMibTable` di `finally`. Return code ≠ `NO_ERROR` → lempar `Win32Exception` dengan kode error (konteks), tanpa data pribadi.
- `ReadAdapters`: `Kind` dari `AdapterClassifier` (`Type`, `InterfaceAndOperStatusFlags.HardwareInterface/FilterInterface`), `IsUp` dari `OperStatus == IfOperStatusUp`. `HasDefaultGateway` dari `System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()`: cocokkan `NetworkInterface.Id` (string GUID) ke `InterfaceGuid`, lalu cek `GetIPProperties().GatewayAddresses.Count > 0`. Lookup lewat `Dictionary<Guid, bool>`, bukan pencarian linear.

- [ ] **Step 1: Tulis integration test yang gagal** (berjalan di Windows nyata, tanpa asumsi adapter tertentu):

```csharp
public class IfTableCounterSourceTests
{
    [Fact] public void ReadCounters_ReturnsUniqueInterfaceIds()
    {
        var rows = new IfTableCounterSource().ReadCounters();
        Assert.NotEmpty(rows);
        Assert.Equal(rows.Count, rows.Select(r => r.InterfaceId).Distinct().Count());
    }

    [Fact] public void ReadCounters_TwoReads_CountersNeverDecreaseForStableInterface()
    {
        var src = new IfTableCounterSource();
        var a = src.ReadCounters().ToDictionary(r => r.InterfaceId);
        var b = src.ReadCounters();
        foreach (var r in b.Where(r => a.ContainsKey(r.InterfaceId)))
        {
            Assert.True(r.BytesReceived >= a[r.InterfaceId].BytesReceived);
            Assert.True(r.BytesSent >= a[r.InterfaceId].BytesSent);
        }
    }

    [Fact] public void ReadAdapters_IdsMatchCounters_AndLoopbackClassified()
    {
        var src = new IfTableCounterSource();
        var adapters = src.ReadAdapters();
        Assert.Equal(src.ReadCounters().Select(c => c.InterfaceId).ToHashSet(), adapters.Select(a => a.Id).ToHashSet());
        Assert.Contains(adapters, a => a.Kind == AdapterKind.Loopback);
    }
}
```

- [ ] **Step 2: Jalankan → FAIL.**
  Run: `dotnet test --filter "FullyQualifiedName~IfTableCounterSourceTests"`
- [ ] **Step 3: Implementasikan `IfTableCounterSource`.**
- [ ] **Step 4: Jalankan → PASS.**
- [ ] **Step 5: Buat `tools/SpeedProbe`** (konsol): tiap 1 detik membaca counter, memakai `SpeedCalculator` + `AdapterSelector` (mode Auto) + `SpeedFormatter`, mencetak `↑ … ↓ …` dan **jenis** adapter yang terpilih (bukan nama/IP). Lalu verifikasi manual: unduh file besar dan bandingkan dengan Task Manager selama 60 detik. Kriteria lulus: selisih < 5% (spec §1). Catat hasil serta daftar Kind adapter di PC uji (termasuk apakah VPN kantor terklasifikasi `Vpn` atau `Virtual`) di `docs/superpowers/spikes/2026-11-xx-speed-accuracy.md`.
  Run: `dotnet run --project tools/SpeedProbe`
- [ ] **Step 6: Verifikasi penuh.** `dotnet build -c Release -warnaserror`, `dotnet test`, `dotnet format --verify-no-changes` → semuanya lolos.
- [ ] **Step 7: Commit (setelah konfirmasi).** `feat: baca counter adapter via GetIfTable2`

---

### Task 6: Spike posisi taskbar (THROWAWAY) — spec §11 langkah 1

**Files:**
- Create: `spikes/TaskbarLocatorSpike/` (proyek WPF terpisah, **tidak** masuk `NetworkSpeed.sln`)
- Create: `docs/superpowers/spikes/2026-11-xx-taskbar-locator-results.md`

Tujuan spike: menjawab apakah Left/Center/Right bisa dihitung **andal** dengan overlay + UI Automation, dan menghasilkan matriks F-07a. Kode spike tidak diberi test dan tidak dipakai ulang apa adanya.

- [ ] **Step 1: Petakan tree UI Automation taskbar** di Windows 10 dan 11 dengan *Accessibility Insights for Windows* atau `inspect.exe` (Windows SDK). Catat ClassName/AutomationId/ControlType elemen yang relevan: tombol Start, Widgets/Search, daftar tombol app, area tray. **Jangan mengandalkan nama yang ditebak**; tulis apa yang benar-benar terlihat.
- [ ] **Step 2: Buat overlay minimal**: jendela WPF borderless, `AllowsTransparency`, topmost, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` (lewat `SetWindowLongPtr`), Per-Monitor DPI v2 di manifest, menampilkan teks statis `↑ 1.2 MB/s ↓ 5.4 MB/s`.
- [ ] **Step 3: Implementasikan locator spike** untuk Left/Center/Right berdasarkan hasil Step 1, plus fallback koordinat `Shell_TrayWnd`/`TrayNotifyWnd` (F-07b). Recalc dipicu oleh: event UIA structure-changed pada daftar tombol app, pesan `TaskbarCreated`, `WM_SETTINGCHANGE`, `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, dan polling 2 detik.
- [ ] **Step 4: Uji skenario berikut** di Windows 10 22H2 dan Windows 11 (masing-masing): perataan kiri & tengah (Win11), taskbar bawah/atas/samping (Win10), DPI 100/150/200%, 2 monitor dengan DPI berbeda, auto-hide, pin/unpin app, buka/tutup 10 app, tray bertambah, Explorer restart (`taskkill /f /im explorer.exe` lalu jalankan lagi), sleep/resume, aplikasi fullscreen, taskbar penuh. Ukur juga: waktu recalc (ms), CPU idle overlay, apakah z-order tetap di atas taskbar saat taskbar diklik.
- [ ] **Step 5: Tulis hasil** di file results: tabel matriks posisi × OS × perataan × orientasi dengan status *andal / tidak andal / tidak didukung* + alasan; elemen UIA yang dipakai; angka pengukuran; risiko yang ditemukan; rekomendasi untuk Plan 2. Tanpa IP/SSID/nama host.
- [ ] **Step 6: Commit (setelah konfirmasi).** `docs: hasil spike posisi taskbar`

---

### Task 7: Spike ETW per aplikasi (THROWAWAY) — spec §11 langkah 2

**Files:**
- Create: `spikes/EtwSpike/` (Worker Service .NET yang bisa dipasang sebagai Windows Service, **tidak** masuk `NetworkSpeed.sln`)
- Modify: `docs/superpowers/spikes/2026-11-xx-etw-results.md`

- [ ] **Step 1: Buat Worker Service** yang membuka real-time ETW session (TraceEvent) untuk provider jaringan kernel `Microsoft-Windows-Kernel-Network` (TCP/UDP send/receive per PID). Setiap 10 detik, tulis ke file di `%ProgramData%\NetworkSpeedSpike\` berisi: jumlah event, total byte per PID, session ID per PID (`ProcessIdToSessionId`), total byte dibanding delta counter adapter (dari `IfTableCounterSource`), CPU dan working set proses.
- [ ] **Step 2: Pengguna (admin) memasang service uji** dengan akun `NT AUTHORITY\LocalService`, lalu menambahkan `LocalService` ke grup *Performance Log Users*. Perintah `sc.exe create` / `net localgroup` disiapkan dan **ditunjukkan ke pengguna untuk dijalankan sendiri**. Ini mengubah konfigurasi sistem, jadi tidak dijalankan otomatis.
- [ ] **Step 3: Uji dengan `LocalService`.** Catat apakah session ETW bisa dibuat; kalau gagal, catat kode error-nya.
- [ ] **Step 4: Kalau Step 3 gagal**, ulangi dengan `LocalSystem` dan catat bedanya.
- [ ] **Step 5: Ukur** pada 3 skenario (unduh besar via browser, Windows Update/aktivitas sistem, idle 30 menit): selisih total per aplikasi vs counter adapter (S-09), proporsi trafik sesi 0 (baris "System" S-05), CPU dan RAM service, dan apakah event hilang (lost events) di trafik tinggi.
- [ ] **Step 6: Tulis hasil + rekomendasi** S-01 (provider final) dan S-03 (akun service). Jangan mencantumkan nama proses milik user lain atau IP.
- [ ] **Step 7: Pengguna menghapus service uji** (`sc.exe delete`) dan mengembalikan keanggotaan grup bila diinginkan. Perintahnya disiapkan, pengguna yang menjalankan.
- [ ] **Step 8: Commit (setelah konfirmasi).** `docs: hasil spike ETW per aplikasi`

---

### Task 8: Go/no-go (spec §11)

**Files:**
- Modify: `docs/superpowers/specs/2026-11-11-network-speed-design.md` (§4.2 F-07a matriks final, §4.6 S-01/S-03 final, tandai keputusan)

- [ ] **Step 1: Laporkan ringkasan kedua spike ke pengguna**, beserta rekomendasi sesuai aturan go/no-go di spec §11.
- [ ] **Step 2: Setelah pengguna menyetujui**, perbarui spec dengan matriks F-07a final serta keputusan S-01/S-03.
- [ ] **Step 3: Tulis Plan 2 dan Plan 3** (bisa paralel karena tidak berbagi file selain `NetworkSpeed.Core`) memakai skill writing-plans.
- [ ] **Step 4: Commit (setelah konfirmasi).** `docs: keputusan go/no-go setelah spike`
