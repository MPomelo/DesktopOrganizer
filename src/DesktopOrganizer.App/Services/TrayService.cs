using System.Drawing;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace DesktopOrganizer.App.Services;

public sealed class TrayService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action _showAction;
    private readonly Action _hideAction;
    private readonly Action _exitAction;
    private readonly ILogger _logger;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _showMenuItem;
    private readonly Forms.ToolStripMenuItem _hideMenuItem;
    private bool _disposed;

    public TrayService(
        Dispatcher dispatcher,
        Action showAction,
        Action hideAction,
        Action exitAction,
        ILogger logger)
    {
        _dispatcher = dispatcher;
        _showAction = showAction;
        _hideAction = hideAction;
        _exitAction = exitAction;
        _logger = logger;

        _showMenuItem = new Forms.ToolStripMenuItem("显示收纳区");
        _showMenuItem.Click += OnShowClicked;
        _hideMenuItem = new Forms.ToolStripMenuItem("隐藏收纳区");
        _hideMenuItem.Click += OnHideClicked;
        var exitMenuItem = new Forms.ToolStripMenuItem("退出");
        exitMenuItem.Click += OnExitClicked;

        var contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add(_showMenuItem);
        contextMenu.Items.Add(_hideMenuItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(exitMenuItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = contextMenu,
            Icon = SystemIcons.Application,
            Text = "DesktopOrganizer"
        };
        _notifyIcon.DoubleClick += OnShowClicked;
    }

    public void Show()
    {
        _notifyIcon.Visible = true;
        _logger.Info("Notification-area icon shown.");
    }

    public void UpdateContainerState(bool hasContainer, bool isVisible)
    {
        _showMenuItem.Text = hasContainer ? "显示收纳区" : "新建收纳区";
        _showMenuItem.Enabled = !isVisible;
        _hideMenuItem.Enabled = hasContainer && isVisible;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        _logger.Info("Exit requested from the notification area.");
        _dispatcher.BeginInvoke(_exitAction);
    }

    private void OnShowClicked(object? sender, EventArgs e)
    {
        _logger.Info("Show or create requested from the notification area.");
        _dispatcher.BeginInvoke(_showAction);
    }

    private void OnHideClicked(object? sender, EventArgs e)
    {
        _logger.Info("Hide requested from the notification area.");
        _dispatcher.BeginInvoke(_hideAction);
    }
}
