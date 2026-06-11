using System;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Windows;
using WinForms = System.Windows.Forms;
using AudioHQ.Core;

namespace AudioHQ.App;

/// <summary>
/// Owns the notification-area (tray) icon and the minimize/close-to-tray behaviour
/// for the main window. The two behaviours are read live from the supplied callbacks
/// so toggling them in Options takes effect immediately, without a restart.
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly Window _window;
    private readonly Func<bool> _minimizeToTray;
    private readonly Func<bool> _closeToTray;
    private readonly WinForms.NotifyIcon _icon;
    private bool _exiting;

    public TrayController(Window window, Func<bool> minimizeToTray, Func<bool> closeToTray)
    {
        _window = window;
        _minimizeToTray = minimizeToTray;
        _closeToTray = closeToTray;

        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = $"AudioHQ v{AppVersion.Display}",
            Visible = true,
        };
        // Single left-click toggles the window in/out of the tray.
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) Toggle(); };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Show AudioHQ", null, (_, _) => Restore());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());
        _icon.ContextMenuStrip = menu;

        _window.StateChanged += OnStateChanged;
    }

    /// <summary>Live hover text for the tray icon (e.g. which outputs are ON/OFF).</summary>
    public void SetTooltip(string text)
    {
        try
        {
            // NotifyIcon.Text is capped (~127 chars); trim defensively so it never throws.
            _icon.Text = text.Length > 127 ? text[..124] + "..." : text;
        }
        catch (Exception ex)
        {
            Log.Write($"TrayController.SetTooltip failed: {ex.Message}");
        }
    }

    private static Icon LoadIcon()
    {
        try
        {
            // app.ico is embedded as a resource (named "<AssemblyName>.app.ico"), so the
            // portable build needs no loose icon file beside the exe.
            var asm = Assembly.GetExecutingAssembly();
            var name = Array.Find(asm.GetManifestResourceNames(),
                n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
            if (name is not null)
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream is not null) return new Icon(stream);
            }
            Log.Write("TrayController: embedded app.ico not found, using default");
        }
        catch (Exception ex)
        {
            Log.Write($"TrayController: app.ico load failed, using default: {ex.Message}");
        }
        return SystemIcons.Application;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_window.WindowState == WindowState.Minimized && _minimizeToTray())
        {
            _window.Hide();           // drop the taskbar button; live in the tray only
            Log.Write("Tray: minimized to tray");
        }
    }

    /// <summary>
    /// Hook from MainWindow.OnClosing. When close-to-tray is on and the user clicked
    /// the X (not the tray Exit item), hide instead of exiting. Returns true if the
    /// close was intercepted.
    /// </summary>
    public bool HandleClosing(CancelEventArgs e)
    {
        if (_exiting || !_closeToTray()) return false;
        e.Cancel = true;
        _window.Hide();
        Log.Write("Tray: close-to-tray, hidden");
        return true;
    }

    /// <summary>Single tray click: hide to tray if shown, restore if hidden/minimized.</summary>
    private void Toggle()
    {
        if (_window.IsVisible && _window.WindowState != WindowState.Minimized)
        {
            _window.Hide();
            Log.Write("Tray: toggled to tray");
        }
        else
        {
            Restore();
        }
    }

    private void Restore()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void Exit()
    {
        _exiting = true;
        _window.Close();
    }

    public void Dispose()
    {
        _window.StateChanged -= OnStateChanged;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
