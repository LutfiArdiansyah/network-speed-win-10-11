// THROWAWAY SPIKE (Plan 1 Task 7, spec §11 langkah 2).
// Menjawab: (1) bisakah akun LocalService membuka sesi ETW real-time Microsoft-Windows-Kernel-Network,
// (2) seberapa dekat total byte per proses dengan counter adapter (S-09), (3) porsi trafik sesi 0 (S-05),
// (4) biaya CPU/RAM dan event yang hilang.
// Privasi: log hanya memuat PID, session ID, dan angka. Tidak ada nama proses, IP, port, atau nama host.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Core.Sampling;
using NetworkSpeed.Interop;

const string SessionName = "NetworkSpeedSpike-Etw";
const string DefaultProvider = "Microsoft-Windows-Kernel-Network";
var reportInterval = TimeSpan.FromSeconds(10);

var minutes = ArgValue("--minutes", 30);
var ProviderName = ArgString("--provider") ?? DefaultProvider;

// Mode administrasi (dipanggil skrip install sebagai admin): atur hak "enable" pada provider ETW untuk satu SID.
if (args.Contains("--print-provider-guid"))
{
    Console.WriteLine(TraceEventProviders.GetProviderGuidByName(DefaultProvider));
    return 0;
}

if (ArgString("--grant-provider-access") is { } grantSid)
{
    var guid = TraceEventProviders.GetProviderGuidByName(DefaultProvider);
    var sid = new SecurityIdentifier(grantSid);
    var sidBytes = new byte[sid.BinaryLength];
    sid.GetBinaryForm(sidBytes, 0);
    const uint EventSecurityAddDACL = 2, TraceLogGuidEnable = 0x0080;
    var rc = Native.EventAccessControl(ref guid, EventSecurityAddDACL, sidBytes, TraceLogGuidEnable, true);
    Console.WriteLine($"EventAccessControl(add enable, {grantSid}) on {guid} -> {rc}");
    return rc == 0 ? 0 : 3;
}

if (args.Contains("--remove-provider-access"))
{
    var guid = TraceEventProviders.GetProviderGuidByName(DefaultProvider);
    var rc = Native.EventAccessRemove(ref guid);
    Console.WriteLine($"EventAccessRemove on {guid} -> {rc}");
    return rc == 0 ? 0 : 3;
}

var outDir = ArgString("--out") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetworkSpeedSpike");
Directory.CreateDirectory(outDir);
var logPath = Path.Combine(outDir, $"etw-{DateTime.Now:yyyyMMdd-HHmmss}.log");
void Log(string m)
{
    var line = $"{DateTime.Now:HH:mm:ss} {m}";
    Console.WriteLine(line);
    File.AppendAllText(logPath, line + Environment.NewLine);
}

var identity = WindowsIdentity.GetCurrent();
var isElevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
var isPerfLogUser = identity.Groups?.Any(g => g.Value == "S-1-5-32-559") == true;
Log($"START account-sid={identity.User?.Value} elevated={isElevated} perfLogUsers={isPerfLogUser} minutes={minutes} os={Environment.OSVersion.Version}");

// [kind] -> {sentBytes, recvBytes}; per PID -> {sent, recv}
var perPid = new ConcurrentDictionary<int, long[]>();
var eventCounts = new ConcurrentDictionary<string, long>();
long loopbackBytes = 0, unknownDirection = 0, malformed = 0;

TraceEventSession session;
try
{
    if (TraceEventSession.GetActiveSessionNames().Contains(SessionName))
    {
        using var stale = new TraceEventSession(SessionName, TraceEventSessionOptions.Attach);
        stale.Stop();
        Log("stopped stale session from previous run");
    }

    session = new TraceEventSession(SessionName) { StopOnDispose = true };
    // Keyword 0x10 = IPv4, 0x20 = IPv6 (manifest Kernel-Network). Level Informational.
    var started = session.EnableProvider(ProviderName, TraceEventLevel.Informational, ProviderName == DefaultProvider ? 0x30UL : ulong.MaxValue);
    Log($"ENABLE-OK restartedExisting={started}");
    Log($"SESSION-OK provider={ProviderName}");
}
catch (Exception ex)
{
    Log($"SESSION-FAILED {ex.GetType().Name} hresult=0x{ex.HResult:X8} message={ex.Message}");
    return 2;
}

session.Source.Dynamic.All += e =>
{
    var name = e.EventName ?? $"id{(int)e.ID}";
    eventCounts.AddOrUpdate(name, 1, (_, c) => c + 1);
    // Nama event Kernel-Network memakai "…Datasent" / "…Datareceived", bukan "Send"/"Recv".
    var isSend = name.Contains("Send", StringComparison.OrdinalIgnoreCase) || name.Contains("sent", StringComparison.OrdinalIgnoreCase);
    var isRecv = name.Contains("Recv", StringComparison.OrdinalIgnoreCase) || name.Contains("Receive", StringComparison.OrdinalIgnoreCase);
    if (!isSend && !isRecv)
    {
        Interlocked.Increment(ref unknownDirection);
        return;
    }

    if (e.PayloadByName("size") is not { } sizeObj || e.PayloadByName("PID") is not { } pidObj)
    {
        Interlocked.Increment(ref malformed);
        return;
    }

    var size = Convert.ToInt64(sizeObj);
    var pid = Convert.ToInt32(pidObj);
    if (IsLoopback(e.PayloadByName("daddr")) || IsLoopback(e.PayloadByName("saddr")))
    {
        Interlocked.Add(ref loopbackBytes, size);
        return;
    }

    var slot = perPid.GetOrAdd(pid, _ => new long[2]);
    Interlocked.Add(ref slot[isSend ? 0 : 1], size);
};

var pump = Task.Run(() => session.Source.Process());

// Pembanding: counter adapter (mode Auto, sama seperti overlay).
var counters = new IfTableCounterSource();
var adapters = counters.ReadAdapters();
var selected = AdapterSelector.Select(adapters, AdapterSelectionMode.Auto, null, includeVpnInAll: false);
Log($"adapters selected(Auto)={selected.Count} kinds={string.Join(",", adapters.Where(a => selected.Contains(a.Id)).Select(a => a.Kind))}");
var baseline = counters.ReadCounters().Where(c => selected.Contains(c.InterfaceId)).ToList();
var self = Process.GetCurrentProcess();
var cpuStart = self.TotalProcessorTime;
var wallStart = Stopwatch.StartNew();
var deadline = TimeSpan.FromMinutes(minutes);
var sessionCache = new Dictionary<int, int>();

while (wallStart.Elapsed < deadline && !pump.IsCompleted)
{
    await Task.Delay(reportInterval);
    var now = counters.ReadCounters().Where(c => selected.Contains(c.InterfaceId)).ToList();
    var (adapterSent, adapterRecv) = Delta(baseline, now);
    var snapshot = perPid.ToArray();
    var etwSent = snapshot.Sum(p => p.Value[0]);
    var etwRecv = snapshot.Sum(p => p.Value[1]);
    RefreshSessions(sessionCache);
    long session0 = 0;
    foreach (var (pid, bytes) in snapshot)
    {
        if (SessionOf(pid, sessionCache) == 0) session0 += bytes[0] + bytes[1];
    }

    self.Refresh();
    var cpuPct = 100 * (self.TotalProcessorTime - cpuStart).TotalSeconds / wallStart.Elapsed.TotalSeconds / Environment.ProcessorCount;
    var top = snapshot.OrderByDescending(p => p.Value[0] + p.Value[1]).Take(5)
        .Select(p => $"{p.Key}/s{SessionOf(p.Key, sessionCache)}:{(p.Value[0] + p.Value[1]) / 1024}KB");
    Log($"REPORT t={wallStart.Elapsed.TotalSeconds:F0}s " +
        $"etw sent={etwSent / 1024}KB recv={etwRecv / 1024}KB | adapter sent={adapterSent / 1024}KB recv={adapterRecv / 1024}KB | " +
        $"ratio sent={Ratio(etwSent, adapterSent)} recv={Ratio(etwRecv, adapterRecv)} | " +
        $"session0={Ratio(session0, etwSent + etwRecv)} loopback={loopbackBytes / 1024}KB pids={snapshot.Length} " +
        $"lost={session.EventsLost} unknownDir={unknownDirection} malformed={malformed} " +
        $"cpu={cpuPct:F2}%(total) privMB={self.PrivateMemorySize64 / 1048576} " +
        $"unknownSessionPids={snapshot.Count(p => SessionOf(p.Key, sessionCache) < 0)} top=[{string.Join(" ", top)}]");
    Log($"EVENTS {string.Join(" ", eventCounts.OrderByDescending(k => k.Value).Take(8).Select(k => $"{k.Key}={k.Value}"))}");
}

Log($"EVENT-COUNTS {string.Join(" ", eventCounts.OrderByDescending(k => k.Value).Select(k => $"{k.Key}={k.Value}"))}");
session.Dispose();
Log("STOP");
return 0;

static (long Sent, long Recv) Delta(List<InterfaceCounterSnapshot> before, List<InterfaceCounterSnapshot> after)
{
    long sent = 0, recv = 0;
    var map = before.ToDictionary(b => b.InterfaceId);
    foreach (var a in after)
    {
        if (!map.TryGetValue(a.InterfaceId, out var b)) continue;
        sent += a.BytesSent >= b.BytesSent ? (long)(a.BytesSent - b.BytesSent) : 0;
        recv += a.BytesReceived >= b.BytesReceived ? (long)(a.BytesReceived - b.BytesReceived) : 0;
    }

    return (sent, recv);
}

static string Ratio(long part, long whole) => whole <= 0 ? "n/a" : $"{100.0 * part / whole:F1}%";

static bool IsLoopback(object? address) =>
    address is not null && IPAddress.TryParse(address.ToString(), out var ip) && IPAddress.IsLoopback(ip);

// Process.GetProcesses() membaca SessionId dari snapshot sistem (NtQuerySystemInformation) tanpa membuka
// handle proses, jadi tetap berfungsi untuk LOCAL SERVICE. ProcessIdToSessionId butuh handle → gagal (-1).
static void RefreshSessions(Dictionary<int, int> cache)
{
    foreach (var p in Process.GetProcesses())
    {
        using (p)
        {
            cache[p.Id] = p.SessionId;
        }
    }
}

static int SessionOf(int pid, Dictionary<int, int> cache) => cache.TryGetValue(pid, out var s) ? s : -1;

int ArgValue(string name, int fallback) =>
    ArgString(name) is { } v && int.TryParse(v, out var n) && n > 0 ? n : fallback;

string? ArgString(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

internal static class Native
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("advapi32.dll")]
    public static extern uint EventAccessControl(ref Guid guid, uint operation, byte[] sid, uint rights, [MarshalAs(UnmanagedType.U1)] bool allowOrDeny);

    [DllImport("advapi32.dll")]
    public static extern uint EventAccessRemove(ref Guid guid);
}
