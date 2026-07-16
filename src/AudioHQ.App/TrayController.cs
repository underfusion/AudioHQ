using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
    private readonly Action? _onMiddleClick;
    private readonly WinForms.NotifyIcon _icon;
    private readonly Icon _baseIcon;
    private Icon? _overlayIcon;
    private bool _exiting;
    private List<Window> _visibleOwnedWindows = new();
    private Window? _activeWindowBeforeHide;

    public TrayController(Window window, Func<bool> minimizeToTray, Func<bool> closeToTray,
        Action? onMiddleClick = null)
    {
        _window = window;
        _minimizeToTray = minimizeToTray;
        _closeToTray = closeToTray;
        _onMiddleClick = onMiddleClick;

        _baseIcon = LoadIcon();
        _icon = new WinForms.NotifyIcon
        {
            Icon = _baseIcon,
            Text = $"AudioHQ v{AppVersion.Display}",
            Visible = true,
        };
        // Left-click toggles the window; middle-click toggles the focused channel.
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) Toggle();
            else if (e.Button == WinForms.MouseButtons.Middle) _onMiddleClick?.Invoke();
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Show AudioHQ", null, (_, _) => Restore());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());
        _icon.ContextMenuStrip = menu;

        _window.StateChanged += OnStateChanged;
    }

    /// <summary>
    /// Update the tray icon to reflect the focused channel state.
    /// Pass true for active (green dot), false or null for plain icon (no dot).
    /// </summary>
    public void SetFocusedChannelState(bool? isActive)
    {
        try
        {
            _overlayIcon?.Dispose();
            _overlayIcon = null;
            if (isActive == true)
            {
                _overlayIcon = BuildGreenDotIcon();
                _icon.Icon = _overlayIcon;
            }
            else
            {
                _icon.Icon = _baseIcon;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"TrayController.SetFocusedChannelState failed: {ex.Message}");
        }
    }

    private Icon BuildGreenDotIcon()
    {
        using var bmp = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var base16 = new Icon(_baseIcon, 16, 16);
            using var baseBmp = base16.ToBitmap();
            g.DrawImage(baseBmp, 0, 0, 16, 16);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            // The app's own green, not GDI's LimeGreen - same "on" meaning as the ON pill.
            using var dotBrush = new SolidBrush(ThemeResources.DrawingColor("Color.Green"));
            g.FillEllipse(dotBrush, 10, 10, 5, 5);
        }
        var handle = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

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
            HideWindowSet();          // drop the full owned window set from the desktop
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
        HideWindowSet();
        Log.Write("Tray: close-to-tray, hidden");
        return true;
    }

    /// <summary>Single tray click: hide to tray if shown, restore if hidden/minimized.</summary>
    private void Toggle()
    {
        if (_window.IsVisible && _window.WindowState != WindowState.Minimized)
        {
            HideWindowSet();
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
        foreach (var owned in _visibleOwnedWindows.ToArray())
        {
            try
            {
                if (owned.Owner == _window && !owned.IsVisible) owned.Show();
            }
            catch (InvalidOperationException ex)
            {
                Log.Write($"Tray: skipped closed owned window '{owned.Title}': {ex.Message}");
            }
        }

        var activate = _activeWindowBeforeHide;
        if (activate is not null && activate.IsVisible) activate.Activate();
        else _window.Activate();
        _visibleOwnedWindows.Clear();
        _activeWindowBeforeHide = null;
    }

    private void HideWindowSet()
    {
        var owned = _window.OwnedWindows.Cast<Window>().Where(w => w.IsVisible).ToList();
        _visibleOwnedWindows = owned;
        _activeWindowBeforeHide = owned.FirstOrDefault(w => w.IsActive) ?? _window;
        foreach (var child in owned) child.Hide();
        _window.Hide();
    }

    private void Exit()
    {
        _exiting = true;
        _visibleOwnedWindows.Clear();
        _window.Close();
    }

    public void Dispose()
    {
        _window.StateChanged -= OnStateChanged;
        _icon.Visible = false;
        _icon.Dispose();
        _overlayIcon?.Dispose();
        _baseIcon.Dispose();
    }
}
