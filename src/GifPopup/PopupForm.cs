namespace GifPopup;

/// <summary>
/// Borderless, topmost, click-through popup shown on the extended screen.
/// Purely visual — opened/closed only via GlobalKeyHook, never interactive.
/// </summary>
public sealed class PopupForm : Form
{
    private readonly AnimatedGifBox _gifBox;
    private readonly System.Windows.Forms.Timer _topmostTimer;

    public PopupForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Visible = false;

        _gifBox = new AnimatedGifBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
        };
        Controls.Add(_gifBox);

        _topmostTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _topmostTimer.Tick += (_, _) => ReassertTopmost();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Click-through + layered styles can only be applied once the
        // window handle exists — doing this in the constructor is a silent no-op.
        var exStyle = NativeMethods.GetWindowLongPtr(Handle, NativeMethods.GWL_EXSTYLE);
        var newStyle = exStyle.ToInt64() | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GWL_EXSTYLE, new IntPtr(newStyle));

        // Required once WS_EX_LAYERED is set, since this window paints via
        // ordinary GDI rather than UpdateLayeredWindow — without this the
        // layered surface never gets composited and the window stays invisible.
        NativeMethods.SetLayeredWindowAttributes(Handle, 0, 255, NativeMethods.LWA_ALPHA);
    }

    /// <summary>Positions the popup on the given screen per the configured size mode, then shows and animates the GIF.</summary>
    public void ShowGif(GifEntry entry, Screen targetScreen, PopupSizeMode sizeMode, int fixedWidth, int fixedHeight)
    {
        if (sizeMode == PopupSizeMode.FullScreen)
        {
            Bounds = targetScreen.Bounds;
        }
        else
        {
            var area = targetScreen.Bounds;
            var width = Math.Min(fixedWidth, area.Width);
            var height = Math.Min(fixedHeight, area.Height);
            var x = area.X + (area.Width - width) / 2;
            var y = area.Y + (area.Height - height) / 2;
            Bounds = new Rectangle(x, y, width, height);
        }

        _gifBox.Image = entry.Image;

        if (!Visible)
        {
            Show();
        }
        TopMost = true;
        _gifBox.StartAnimating();
        _topmostTimer.Start();
        ReassertTopmost();
    }

    /// <summary>Stops animation and hides the popup.</summary>
    public void HideAndStop()
    {
        _topmostTimer.Stop();
        _gifBox.StopAnimating();
        _gifBox.Image = null;
        Hide();
    }

    private void ReassertTopmost()
    {
        if (!Visible)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }
}
