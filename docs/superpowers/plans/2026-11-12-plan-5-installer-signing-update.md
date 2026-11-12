# Plan 5 — Installer, Code Signing, dan Cek Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** IT bisa memasang, meng-upgrade, dan meng-uninstall Network Speed secara silent dengan satu installer `.exe` bertanda tangan self-signed. App memberi tahu pengguna bila ada versi baru di shared folder kantor.

**Architecture:**
- **Installer:** Inno Setup 6 (sudah terpasang di PC build). Script `.iss` memanggil `sc.exe`/`icacls` untuk service dan ACL.
- **Bundling:** App dan Service dipublish **self-contained win-x64** ke subfolder terpisah (`app\`, `service\`) agar runtime keduanya tidak bertabrakan.
- **Penutupan App yang sopan saat upgrade/uninstall:** lewat event bernama `Global\NetworkSpeed.AppExit`.
  - Event dibuat oleh **service** (LocalService punya hak membuat objek Global; user biasa tidak), dengan ACL: Admin/SYSTEM boleh set, user hanya boleh menunggu.
  - Installer men-set event itu, menunggu, lalu memakai `taskkill` sebagai fallback.
- **Cek update:** logika murni di Core (parse `latest.json`, bandingkan versi, validasi path); App hanya membaca file di thread pool dengan timeout.

**Tech Stack:** C# / .NET 10, WPF, xUnit v3, Inno Setup 6 (ISCC), PowerShell 5.1 (kompatibel dengan PC IT). **Tidak ada paket NuGet baru.**

**Spec:** `docs/superpowers/specs/2026-11-11-network-speed-design.md` (§5 HKLM, §6, §8, §9, §10, S-03, S-07, H-05).

## Global Constraints

- **Plan sebelumnya:** semua constraint Plan 1–4 tetap berlaku: `TreatWarningsAsErrors`, InvariantCulture, tanpa commit kecuali diminta, tanpa atribusi AI, teks UI bahasa Inggris, dokumentasi bahasa Indonesia.
- **Lokasi install:** `C:\Program Files\NetworkSpeed\` dengan subfolder `app\` dan `service\`; x64 saja; installer per-machine yang butuh admin.
- **Service:**
  - nama `NetworkSpeedService`, akun `NT AUTHORITY\LocalService`;
  - start `auto` (bukan delayed, §6);
  - recovery: restart setelah 1 menit, 3 kali (S-07).
- **Folder data PC** `%ProgramData%\NetworkSpeed\`:
  - owner = Administrators;
  - ACL: SYSTEM dan Administrators Full, LocalService Modify, user biasa **tanpa akses** (H-05);
  - perintahnya sama dengan `tools/dev-service.ps1`.
- **Autostart:** `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`, nilai `NetworkSpeed` = `"<dir>\app\NetworkSpeed.App.exe" --autostart`.
- **Pengaturan tingkat PC:** `HKLM\SOFTWARE\NetworkSpeed`, nilai `UpdateFolder` (REG_SZ).
  - Ditulis hanya bila parameter `/UPDATEFOLDER=` diberikan.
  - Upgrade tanpa parameter mempertahankan nilai lama.
- **Parameter silent** (didokumentasikan di README):
  - `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`;
  - `/UPDATEFOLDER="\\server\share\NetworkSpeed"`;
  - `/MERGETASKS="!startmenu"` = tanpa shortcut;
  - uninstaller: `/VERYSILENT /REMOVEDATA` = hapus `%ProgramData%\NetworkSpeed\`. Data `%LOCALAPPDATA%` tiap user **tidak pernah** dihapus.
- **Downgrade ditolak**, dengan exit code non-zero dan pesan di log installer.
- **Versi:** satu sumber, `<Version>` di `Directory.Build.props` (mulai `1.0.0`), dan bisa ditimpa `-Version` di skrip build.
- **Signing:**
  - sertifikat self-signed, EKU Code Signing saja, berlaku 3 tahun [ASSUMPTION spec §9];
  - SHA-256 dengan timestamp RFC 3161 `http://timestamp.digicert.com` [ASSUMPTION: server publik; satu-satunya panggilan keluar saat build];
  - yang ditandatangani hanya file milik proyek (`NetworkSpeed.*.exe/.dll`, installer, uninstaller), bukan DLL runtime Microsoft;
  - `.pfx` **ditolak** bila berada di dalam folder repo, dan password-nya lewat env `NETWORKSPEED_SIGN_PASSWORD` atau prompt.
- **`latest.json`:**
  - maksimal 16 KB, UTF-8;
  - field `version` (`x.y.z` atau `x.y.z.w`), `installer` (nama file saja, `^[A-Za-z0-9._-]{1,100}\.exe$`), `sha256` (64 hex), `notes` (≤ 500 karakter, opsional);
  - field tidak dikenal diabaikan.
- **Cek update:** saat start (+30 detik) lalu tiap 24 jam, timeout baca 10 detik, tidak pernah di UI thread.
  - Gagal = diam, hanya log tanpa path.
  - Toast `A new version (x.y.z) is available. Your IT team will install it.` sekali per versi per user, lewat `state.json`, kunci `update:<versi>`.
- **Tombol "Install now" untuk admin lokal (§10 opsional) tidak dikerjakan di plan ini** [keputusan perancang: IT yang memasang; mengurangi permukaan serangan]. Karena itu pin thumbprint juga tidak diperlukan.

## Review Focus

1. **`latest.json` rusak atau berbahaya** (kosong, > 16 KB, BOM, versi `1.2`/`v1.2.3`/`999999999.0.0`, nama installer `..\evil.exe` atau `C:\x.exe`, sha256 pendek) → diabaikan tanpa crash atau toast, dan log tidak memuat path. Dipin di Task 1.
2. **Shared folder tidak terjangkau atau menggantung** (VPN mati, UNC lambat) → UI tidak macet, tidak ada dialog, dicoba lagi 24 jam kemudian. Dipin di Task 3 (`UpdateMonitor` dengan reader palsu yang menggantung → timeout).
3. **Upgrade saat App berjalan di beberapa sesi**, dengan atau tanpa service berjalan → App ditutup lewat event (atau `taskkill` sebagai fallback), file terganti, tanpa prompt reboot di mode silent. Dipin di Task 6 (uji manual 2 sesi atau `runas`) dan Task 4 (listener).
4. **Event keluar disalahgunakan**: user biasa mencoba men-set `Global\NetworkSpeed.AppExit` → ditolak (ACL), App user lain tetap hidup; event tidak ada → App jalan normal dan mencoba lagi saat service tersedia. Dipin di Task 4.
5. **Upgrade mempertahankan data**: `UpdateFolder` di HKLM, `history.db`, `settings.json`/`state.json` tetap; downgrade ditolak. Dipin di Task 6 (uji manual).

**Urutan eksekusi:** 1 → 2 → 3 → 4 → 5 → 6 → 7.

---

## Struktur file

```
Directory.Build.props                         # + <Version>1.0.0</Version>
.gitignore                                    # + artifacts/
src/NetworkSpeed.Core/
  Update/UpdateManifest.cs                    # parse + validasi latest.json
  Update/UpdateEvaluator.cs                   # versi saat ini vs manifest → status
  Update/UpdateFolderPath.cs                  # validasi nilai HKLM UpdateFolder
  Lifecycle/AppExitSignal.cs                  # nama event + SDDL (dipakai service & App)
  Settings/UserSettings.cs, SettingsSerializer.cs   # + CheckForUpdates (default true)
src/NetworkSpeed.Interop/
  Lifecycle/GlobalEventNative.cs              # CreateEvent dengan SDDL, OpenEvent(SYNCHRONIZE)
src/NetworkSpeed.Service/Workers/AppExitSignalHost.cs   # membuat & memegang event selama service hidup
src/NetworkSpeed.App/
  Features/UpdateMonitor.cs                   # timer 24 jam, baca file di thread pool + timeout, toast
  Features/AppExitListener.cs                 # tunggu event → Shutdown
  Platform/MachineSettings.cs                 # baca HKLM\SOFTWARE\NetworkSpeed (read-only)
  Settings/SettingsWindow.xaml(.cs)           # + grup "Updates"
installer/NetworkSpeed.iss                    # Inno Setup
tools/build-release.ps1                       # test → publish self-contained → sign → ISCC → sign → latest.json
tools/signing/New-CodeSigningCert.ps1
tools/signing/Sign-Files.ps1
docs/it-guide.md                              # README untuk IT (install, silent, verifikasi, sertifikat, uninstall)
```

---

### Task 1: Manifest update & validasi path (§10, Review Focus 1)

**Files:**
- Create `src/NetworkSpeed.Core/Update/{UpdateManifest,UpdateEvaluator,UpdateFolderPath}.cs`.
- Test `tests/NetworkSpeed.Core.Tests/Update/{UpdateManifestTests,UpdateEvaluatorTests,UpdateFolderPathTests}.cs`.

**Interfaces — Produces:**
- `public sealed record UpdateManifest(Version Version, string InstallerFileName, string Sha256, string Notes)`
- `public static UpdateManifest? UpdateManifest.TryParse(ReadOnlySpan<byte> utf8Json)`: `null` bila tidak valid. Konstanta `MaxBytes = 16 * 1024`.
- `public enum UpdateState { UpToDate, Available }`
- `public static UpdateState UpdateEvaluator.Evaluate(Version current, UpdateManifest manifest)`: hanya membandingkan `Major.Minor.Build` (Revision −1 dianggap 0).
- `public static string? UpdateFolderPath.Normalize(string? raw)`: `null` bila kosong atau tidak valid. Path harus fully qualified (UNC `\\server\share\...` atau `X:\...`), tanpa segmen `..`, panjang ≤ 260. Spasi di ujung dipangkas.

- [ ] **Step 1: Tulis test.**
  - `TryParse_Valid_ReturnsManifest`; `TryParse_Utf8Bom_Accepted`; `TryParse_UnknownFields_Ignored`.
  - `TryParse_Invalid_ReturnsNull` (Theory) untuk: kosong, bukan object, `version` `"1.2"` / `"v1.2.3"` / `"1.2.3-beta"` / `"99999999999.0.0"`, `installer` `"..\\evil.exe"` / `"C:\\x.exe"` / `"setup.msi"` / `""`, `sha256` 63 karakter atau non-hex, `notes` 501 karakter.
  - `TryParse_OverMaxBytes_ReturnsNull`.
  - `Evaluate_Newer_Available`, `Evaluate_SameOrOlder_UpToDate` (termasuk `1.0.0` vs `1.0.0.0`).
  - `Normalize_*`: UNC valid; drive valid; relatif → null; `\\server\share\..\x` → null; spasi dipangkas; > 260 → null.
- [ ] **Step 2: Jalankan.** Expected: RED, CS0246 / CS0234.
- [ ] **Step 3: Implementasi.** Pakai `JsonDocument` (tanpa source-gen; cukup kecil). `sha256` disimpan lowercase.
- [ ] **Step 4: Jalankan.** `dotnet test --project tests/NetworkSpeed.Core.Tests --filter-class "*Update*"` → GREEN.

### Task 2: Setting per user `CheckForUpdates` & versi (§5, §10)

**Files:**
- Modify `Directory.Build.props` (`<Version>1.0.0</Version>`), `UserSettings.cs`, `SettingsSerializer.cs`.
- Test `SettingsSerializerTests`.

**Interfaces — Produces:** `public bool CheckForUpdates { get; init; } = true;`

- [ ] **Step 1: Test.**
  - `Deserialize_MissingCheckForUpdates_DefaultsTrue` (file lama tetap terbaca);
  - `Deserialize_CheckForUpdatesFalse_Read`;
  - `Deserialize_CheckForUpdatesWrongType_DefaultsTrue`.
- [ ] **Step 2:** RED, lalu implementasi, lalu GREEN. `SchemaVersion` tetap 1.

### Task 3: `UpdateMonitor` & grup Settings "Updates" (§10, Review Focus 2)

**Files:**
- Create `src/NetworkSpeed.App/Features/UpdateMonitor.cs`, `src/NetworkSpeed.App/Platform/MachineSettings.cs`.
- Modify `SettingsWindow.xaml(.cs)`, `App.xaml.cs`.
- Test `tests/NetworkSpeed.App.Tests/UpdateMonitorTests.cs`.

**Interfaces:**
- Consumes: Task 1, Task 2, `NotificationStateStore` (Plan 4), `TrayIconController.ShowBalloon`.
- Produces:
  - `public interface IManifestReader { Task<byte[]?> ReadAsync(string folder, CancellationToken ct); }` (produksi: `File` di `Task.Run`, membaca maksimal `MaxBytes + 1` byte).
  - `public sealed class UpdateMonitor(IManifestReader reader, Func<string?> updateFolder, Version current, NotificationStateStore state, Action<string,string> showBalloon, IAppLog log, TimeSpan? readTimeout = null) : IDisposable` dengan `Task<UpdateCheckResult> CheckAsync(CancellationToken)`, `string StatusText`, dan `Start()` (timer 30 detik → lalu 24 jam).
  - `public enum UpdateCheckResult { Disabled, NotConfigured, Unreachable, Invalid, UpToDate, Available }`.

- [ ] **Step 1: Test** dengan reader dan store palsu (folder sementara):
  - `Check_FolderNotConfigured_NotConfigured_NoBalloon`;
  - `Check_ReaderHangs_TimesOutAsUnreachable` (timeout 100 ms);
  - `Check_ReaderThrowsIOException_Unreachable`;
  - `Check_InvalidManifest_Invalid_NoBalloon`;
  - `Check_NewerVersion_BalloonOnce_ThenNotAgain` (dua kali cek → satu balloon; `state.json` berisi `update:1.1.0`);
  - `Check_SameVersion_UpToDate`;
  - `Check_Disabled_NoRead`.
- [ ] **Step 2:** RED, lalu implementasi, lalu GREEN.
  - Teks status: `Up to date (1.0.0)` / `Version 1.1.0 is available. Your IT team will install it.` / `Update folder not reachable` / `Not configured by IT`.
  - Log hanya berisi hasil (`Update check: Unreachable`), tanpa path.
- [ ] **Step 3: App.**
  - `MachineSettings.UpdateFolder` membaca `HKLM\SOFTWARE\NetworkSpeed\UpdateFolder` lewat `Registry.LocalMachine` dengan view 64-bit, lalu `UpdateFolderPath.Normalize`.
  - Grup Settings "Updates" berisi checkbox `Check for new versions`, teks read-only `Update folder (set by IT): …` dan status.
  - Wiring di `App.OnStartup`; `Dispose` di `OnExit`.
- [ ] **Step 4:** Build `-warnaserror`, suite, format.

### Task 4: Event keluar `Global\NetworkSpeed.AppExit` (§8, Review Focus 3–4)

**Files:**
- Create `src/NetworkSpeed.Core/Lifecycle/AppExitSignal.cs`, `src/NetworkSpeed.Interop/Lifecycle/GlobalEventNative.cs`, `src/NetworkSpeed.Service/Workers/AppExitSignalHost.cs`, `src/NetworkSpeed.App/Features/AppExitListener.cs`.
- Modify Service `Program.cs`, `App.xaml.cs`.
- Test `tests/NetworkSpeed.Interop.Tests/GlobalEventNativeTests.cs`, `tests/NetworkSpeed.App.Tests/AppExitListenerTests.cs`.

**Interfaces — Produces:**
- `AppExitSignal.Name = @"Global\NetworkSpeed.AppExit"`.
- `AppExitSignal.Sddl = "D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;LS)(A;;0x00100000;;;AU)"`: SYSTEM/Admin/LocalService akses penuh, Authenticated Users hanya SYNCHRONIZE. LocalService wajib ada karena service yang restart setelah crash membuka ulang objek event yang masih dipegang App.
- `GlobalEventNative.Create(string name, string sddl) : SafeWaitHandle` (manual-reset, awalnya non-signaled).
- `GlobalEventNative.TryOpenForWait(string name) : WaitHandle?` (hanya `SYNCHRONIZE`).
- `AppExitListener(Func<WaitHandle?> open, Action onExit)` dengan `TryAttach()` (dipanggil saat start dan setiap `ServiceConnection.StateChanged`) dan `Dispose()`. Memakai `ThreadPool.RegisterWaitForSingleObject`.

- [ ] **Step 1: Test** (nama `Local\NetworkSpeed.Test.<guid>` agar tidak butuh hak Global):
  - `Create_ThenOpenForWait_SetObservedByWaiter`;
  - `OpenForWait_HandleCannotSet` (`SetEvent` pada handle hasil buka → gagal `ERROR_ACCESS_DENIED`);
  - `TryOpenForWait_Missing_ReturnsNull`;
  - `Listener_EventSet_CallsOnExitOnce`;
  - `Listener_OpenReturnsNull_ThenAvailable_AttachesLater`;
  - `Listener_Dispose_NoCallback`.
  - `Create_Twice_WhileFirstHandleOpen_Succeeds` (simulasi service restart saat App masih memegang handle).
- [ ] **Step 2:** RED, lalu implementasi, lalu GREEN.
  - Service membuat event di `AppExitSignalHost` (`IHostedService`) dan memegangnya sampai stop. Gagal membuat → log Warn, service tetap jalan.
  - App: `onExit` → `Dispatcher.BeginInvoke(Shutdown)` dengan log `Exit requested by installer.`
- [ ] **Step 3:** Build, suite, format.

### Task 5: Skrip signing (§9)

**Files:** Create `tools/signing/New-CodeSigningCert.ps1`, `tools/signing/Sign-Files.ps1`.

- **`New-CodeSigningCert.ps1 -OutDir <di luar repo> [-Subject "CN=KCT Network Speed Code Signing"] [-Years 3]`:**
  - `New-SelfSignedCertificate -Type CodeSigningCert -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy Exportable -CertStoreLocation Cert:\CurrentUser\My`;
  - export `.pfx` dengan password dari prompt (SecureString, dua kali); export `.cer`;
  - hapus sertifikat dari store setelah export;
  - cetak thumbprint;
  - tolak `-OutDir` di dalam repo.
- **`Sign-Files.ps1 -PfxPath <path> -Files <string[]> [-TimestampServer url]`:**
  - password dari env `NETWORKSPEED_SIGN_PASSWORD` atau prompt;
  - `Set-AuthenticodeSignature -HashAlgorithm SHA256 -TimestampServer`;
  - gagal bila status bukan `Valid`/`UnknownError` karena root belum dipercaya [catatan: self-signed yang belum diimpor memberi `UnknownError` pada verifikasi lokal]; exit code non-zero bila ada yang gagal;
  - tolak `.pfx` di dalam repo.

- [ ] **Step 1:** Tulis kedua skrip.
- [ ] **Step 2: Verifikasi manual.** Buat sertifikat uji di folder scratch, tanda tangani salinan `NetworkSpeed.App.exe`. `Get-AuthenticodeSignature` menunjukkan signer = subject uji dan `TimeStamperCertificate` terisi. Hapus sertifikat uji setelahnya.

### Task 6: Installer Inno Setup & skrip build (§8, Review Focus 3, 5)

**Files:** Create `installer/NetworkSpeed.iss`, `tools/build-release.ps1`; Modify `.gitignore` (`artifacts/`).

**`build-release.ps1 -Version x.y.z [-PfxPath path] [-SkipTests]`:**
1. `dotnet test`, lalu `dotnet build -c Release -warnaserror`.
2. `dotnet publish` App dan Service: `-c Release -r win-x64 --self-contained true -p:Version=$Version`, ke `artifacts/publish/app` dan `artifacts/publish/service`.
3. Bila `-PfxPath` diberikan, tanda tangani file `NetworkSpeed.*.exe/.dll`.
4. `ISCC.exe installer\NetworkSpeed.iss /DAppVersion=$Version /DPublishDir=...`. Bila signing aktif, tambahkan `/Snssign=...Sign-Files.ps1 ... $f` agar installer dan uninstaller ikut ditandatangani (`SignedUninstaller=yes`).
5. Tulis `artifacts/release/NetworkSpeedSetup-$Version.exe` + `latest.json` (sha256 dari `Get-FileHash`), yang divalidasi ulang dengan aturan Task 1.

Tanpa `-PfxPath`, build tetap jalan tetapi mencetak peringatan "UNSIGNED".

**`NetworkSpeed.iss`:**
- **Identitas:** `AppId` GUID tetap; `PrivilegesRequired=admin`; `ArchitecturesAllowed=x64compatible`, `ArchitecturesInstallIn64BitMode=x64compatible`; `MinVersion=10.0.19045`; `DefaultDirName={autopf}\NetworkSpeed`; `DisableDirPage=yes` (lokasi tetap, §8); `CloseApplications=no` (ditangani sendiri); `UninstallDisplayIcon`.
- **`[Tasks]`:** `startmenu` (default aktif).
- **`[Registry]`:**
  - nilai Run (`uninsdeletevalue`);
  - `HKLM\SOFTWARE\NetworkSpeed\UpdateFolder`, hanya bila `{param:UPDATEFOLDER}` tidak kosong (`Check:`), dan bukan `uninsdeletekey` agar upgrade tetap aman (dihapus di uninstall lewat `[Code]`).
- **`[Code]`:**
  - `InitializeSetup`: tolak downgrade (bandingkan dengan `DisplayVersion` versi terpasang) → log + `Result := False`.
  - `PrepareToInstall` (juga di upgrade):
    1. `SetEvent` pada `Global\NetworkSpeed.AppExit` lewat `OpenEvent(EVENT_MODIFY_STATE)` (Windows API dari `kernel32.dll`).
    2. Tunggu sampai tidak ada `NetworkSpeed.App.exe` (dicek dengan `tasklist`) maksimal 15 detik.
    3. Fallback `taskkill /F /IM NetworkSpeed.App.exe`.
    4. `sc stop NetworkSpeedService` dan tunggu sampai `STOPPED` maksimal 30 detik.
  - `CurStepChanged(ssPostInstall)`:
    1. Folder `%ProgramData%\NetworkSpeed` + `icacls` persis seperti `dev-service.ps1` (setowner Administrators dulu).
    2. `sc create` (atau `sc config` bila sudah ada) dengan `binPath= "<dir>\service\NetworkSpeed.Service.exe" obj= "NT AUTHORITY\LocalService" start= auto`.
    3. `sc failure ... reset= 86400 actions= restart/60000/restart/60000/restart/60000`; `sc description`.
    4. `sc start`.
  - Uninstall (`usUninstall`): langkah penutupan yang sama, lalu `sc stop` dan `sc delete`; hapus kunci `HKLM\SOFTWARE\NetworkSpeed`. Di `usPostUninstall`, bila `/REMOVEDATA` ada di command line, hapus `%ProgramData%\NetworkSpeed`.
- **`[Run]`:** opsi postinstall `Launch Network Speed` (`runasoriginaluser`, `skipifsilent`).
- **`[Icons]`:** Start Menu `Network Speed`, hanya dengan task `startmenu`.
- **Exit code:** mengikuti Inno Setup (0 sukses; 1 init gagal, termasuk downgrade ditolak; 2/5 dibatalkan; 3/4 error fatal; 7/8 persiapan gagal / perlu restart). Perilaku pasti diverifikasi di Step 3 dan dicatat di `docs/it-guide.md` [ASSUMPTION sampai terverifikasi].

- [ ] **Step 1:** Tulis `.iss` dan `build-release.ps1`.
- [ ] **Step 2:** `tools/build-release.ps1 -Version 1.0.0` (tanpa signing, lalu dengan sertifikat uji) → installer + `latest.json` jadi; ISCC tanpa warning.
- [ ] **Step 3: Uji manual oleh pemilik PC (admin).** Disarankan di **Windows Sandbox/VM**; bila di PC ini, uninstall dulu dev service (`tools\dev-service.ps1 -Uninstall`). Catat exit code setiap langkah:
  1. Silent install dengan `/UPDATEFOLDER=` dan `/MERGETASKS="!startmenu"`. Cek service (akun, start auto, recovery), ACL ProgramData (user biasa ditolak), nilai Run, HKLM UpdateFolder, tidak ada shortcut.
  2. Login, App autostart, dan riwayat/per aplikasi berfungsi dengan build self-contained. Ukur memori private App.
  3. Upgrade 1.0.0 → 1.0.1 silent tanpa `/UPDATEFOLDER`, saat App berjalan di sesi aktif: App menutup diri lewat event (log `Exit requested by installer.`), tanpa prompt reboot, UpdateFolder tetap, `history.db` tetap.
  4. Ulangi upgrade dengan service dihentikan dulu → fallback `taskkill` bekerja.
  5. Jalankan installer 1.0.0 di atas 1.0.1 → ditolak (exit code non-zero).
  6. User biasa mencoba men-set event (`[System.Threading.EventWaitHandle]::OpenExisting('Global\NetworkSpeed.AppExit').Set()`) → `UnauthorizedAccessException`.
  7. Uninstall silent → service dan file hilang, data tetap. Lalu install + uninstall `/REMOVEDATA` → `%ProgramData%\NetworkSpeed` hilang.
  8. Cek update: `latest.json` 1.0.1 di folder → toast sekali; restart App → tidak muncul lagi.

### Task 7: Panduan IT & dokumentasi (§8–§10)

**Files:** Create `docs/it-guide.md`; Modify `README.md`, `CLAUDE.md` (perintah build rilis), spec §8 (catat Inno Setup dan exit code terverifikasi).

- [ ] **Step 1:** `docs/it-guide.md` berisi:
  - perintah install/upgrade/uninstall silent beserta semua parameter dan tabel exit code;
  - impor `.cer` ke *Trusted Root* dan *Trusted Publishers* (Local Machine), manual, perintah `certutil`/`Import-Certificate`;
  - verifikasi sebelum distribusi (`Get-AuthenticodeSignature`, `Get-FileHash` vs `latest.json`);
  - struktur shared folder dan rekomendasi hak tulis hanya untuk admin rilis;
  - risiko kebocoran private key dan rotasi sertifikat;
  - lokasi data dan log.
- [ ] **Step 2:** Review akhir oleh reviewer independen, lalu fix pass sesuai skill executing-plans.
