using System.Drawing;
using Forms = System.Windows.Forms;

namespace StandDesk.App.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _trayImage;
    private readonly Forms.ContextMenuStrip _menu = new();
    public TrayService(Action open, Action<string> action, Action exit)
    {
        _menu.Items.Add("Open StandDesk", null, (_, _) => open());
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Sit", null, (_, _) => action("sit"));
        _menu.Items.Add("Stand", null, (_, _) => action("stand"));
        _menu.Items.Add("Stop", null, (_, _) => action("stop"));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => exit());
        using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Resources/StandDesk.ico"))?.Stream
            ?? throw new InvalidOperationException("The StandDesk icon resource is missing.");
        using var sourceIcon = new Icon(stream, Forms.SystemInformation.SmallIconSize);
        _trayImage = (Icon)sourceIcon.Clone();
        _icon = new Forms.NotifyIcon { Icon = _trayImage, Text = "StandDesk — DESK 2079", ContextMenuStrip = _menu, Visible = true };
        _icon.DoubleClick += (_, _) => open();
    }
    public void Update(decimal? height, bool canMove)
    {
        _icon.Text = height.HasValue ? $"StandDesk — {height:0.00} cm" : "StandDesk — DESK 2079";
        _menu.Items[2].Text = "Sit";
        _menu.Items[3].Text = "Stand";
        _menu.Items[2].Enabled = _menu.Items[3].Enabled = canMove;
    }
    public void Dispose() { _icon.Visible = false; _icon.Dispose(); _trayImage.Dispose(); _menu.Dispose(); }
}
