using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using NetworkSpeed.App.Composition;

namespace NetworkSpeed.App.Tray;

/// <summary>Ikon tray dan menunya (spec F-08). Item yang belum berfungsi tidak ditampilkan.</summary>
public sealed class TrayIconController : IDisposable
{
    private const string AppTitle = "Network Speed";
    private readonly NotifyIcon _icon;
    private readonly AppHost _host;

    public TrayIconController(AppHost host, Action exit)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(exit);
        _host = host;
        Menu = new ContextMenuStrip();
        Menu.Items.Add("Exit", null, (_, _) => exit());
        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = AppTitle,
            ContextMenuStrip = Menu,
            Visible = true,
        };
    }

    /// <summary>Menu bersama tray dan overlay; item ditambahkan oleh fitur lain.</summary>
    public ContextMenuStrip Menu { get; }

    /// <summary>Aksi pembuka Settings; diisi saat jendela Settings tersedia.</summary>
    public Action? ShowSettingsAction { get; set; }

    public void ShowSettings() => ShowSettingsAction?.Invoke();

    /// <summary>Menambah item menu di atas pemisah sebelum "Exit".</summary>
    public void AddMenuItem(string text, Action onClick)
    {
        ArgumentNullException.ThrowIfNull(onClick);
        if (Menu.Items.Count == 1)
        {
            Menu.Items.Insert(0, new ToolStripSeparator());
        }

        Menu.Items.Insert(Menu.Items.Count - 2, new ToolStripMenuItem(text, null, (_, _) => onClick()));
    }

    /// <summary>Menampilkan menu di posisi kursor (klik kanan overlay).</summary>
    public void ShowMenuAtCursor() => Menu.Show(Cursor.Position);

    public void ShowBalloon(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(5000);
    }

    /// <summary>Tooltip tray dibatasi Windows 127 karakter.</summary>
    public void SetToolTip(string text) => _icon.Text = text.Length > 127 ? text[..127] : text;

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        Menu.Dispose();
        _ = _host;
    }

    private static Icon LoadIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        return resource is null ? SystemIcons.Application : new Icon(resource.Stream, SystemInformation.SmallIconSize);
    }
}
