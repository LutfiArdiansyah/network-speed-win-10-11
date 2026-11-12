using System.Windows;

namespace TaskbarLocatorSpike;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var position = OverlayPosition.Right;
        foreach (var arg in e.Args)
        {
            if (arg.StartsWith("--position=", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<OverlayPosition>(arg["--position=".Length..], ignoreCase: true, out var parsed))
            {
                position = parsed;
            }
        }

        DispatcherUnhandledException += (_, args) =>
        {
            SpikeLog.Write($"UNHANDLED {args.Exception.GetType().Name}: {args.Exception.Message}");
            args.Handled = true;
        };

        MainWindow = new OverlayWindow(position);
        MainWindow.Show();
    }
}
