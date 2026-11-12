using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Core.Ipc;
using NetworkSpeed.Core.Sampling;

namespace NetworkSpeed.Core.Tests.Ipc;

public class HistoryRequestFactoryTests
{
    private static AdapterInfo Ad(int n, AdapterKind kind, bool gateway = true) => new(new Guid(n, 0, 0, new byte[8]), "nic" + n, kind, true, gateway);

    [Fact]
    public void AdapterIds_Auto_UsesServiceList()
    {
        AdapterInfo[] service = [Ad(1, AdapterKind.Wireless), Ad(2, AdapterKind.Ethernet, gateway: false)];
        Assert.Equal([service[0].Id], HistoryRequestFactory.AdapterIds(service, new AdapterSelection(AdapterSelectionMode.Auto, null, false)));
    }

    [Fact]
    public void AdapterIds_SpecificMissing_Null()
        => Assert.Null(HistoryRequestFactory.AdapterIds([Ad(1, AdapterKind.Wireless)], new AdapterSelection(AdapterSelectionMode.Specific, Guid.NewGuid(), false)));

    [Fact]
    public void AdapterIds_EmptyServiceList_Null()
        => Assert.Null(HistoryRequestFactory.AdapterIds([], new AdapterSelection(AdapterSelectionMode.Auto, null, false)));

    [Fact]
    public void AdapterIds_Over64_Capped()
    {
        var service = Enumerable.Range(1, 80).Select(n => Ad(n, AdapterKind.Ethernet)).ToList();
        var ids = HistoryRequestFactory.AdapterIds(service, new AdapterSelection(AdapterSelectionMode.All, null, false))!;
        Assert.Equal(64, ids.Count);
        Assert.Equal(ids.Order(), ids);
    }

    [Fact]
    public void Minutes_7Days_PassesValidator()
    {
        var now = new DateTime(2026, 11, 12, 10, 15, 30);
        var request = HistoryRequestFactory.Minutes([Guid.NewGuid()], now.AddDays(-7).AddMinutes(1), now);
        Assert.Null(PipeRequestValidator.Validate(request));
    }

    [Fact]
    public void DailyTotals_Month_PassesValidator()
        => Assert.Null(PipeRequestValidator.Validate(HistoryRequestFactory.DailyTotals([Guid.NewGuid()], new DateOnly(2026, 10, 15), new DateOnly(2026, 11, 14))));
}
