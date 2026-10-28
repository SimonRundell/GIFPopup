namespace GifPopup;

/// <summary>
/// The teacher-facing control panel: tile grid of GIF previews with letter
/// badges, rescan/monitor/size controls, and status label. Stays topmost
/// over PowerPoint Presenter View. Position/size are persisted to config.json.
/// </summary>
public sealed class ControlForm : Form
{
    private readonly AppConfig _config;
    private readonly GifBank _bank;

    private readonly FlowLayoutPanel _tilePanel;
    private readonly Label _statusLabel;
    private readonly System.Windows.Forms.Timer _topmostTimer;
    private readonly System.Windows.Forms.Timer _saveDebounceTimer;

    public event Action? RescanRequested;

    public ControlForm(AppConfig config, GifBank bank)
    {
        _config = config;
        _bank = bank;

        Text = "GifPopup";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(_config.ControlWindow.X, _config.ControlWindow.Y, _config.ControlWindow.Width, _config.ControlWindow.Height);
        FormBorderStyle = FormBorderStyle.Sizable;
        TopMost = true;

        var toolStrip = new ToolStrip();
        var rescanButton = new ToolStripButton("Rescan GIF folder");
        rescanButton.Click += (_, _) => RescanRequested?.Invoke();
        var openFolderButton = new ToolStripButton("Open GIF folder");
        openFolderButton.Click += (_, _) => OpenGifFolder();
        toolStrip.Items.Add(rescanButton);
        toolStrip.Items.Add(openFolderButton);

        _tilePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
        };

        _statusLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            Text = "Ready",
            TextAlign = ContentAlignment.MiddleLeft,
        };

        Controls.Add(_tilePanel);
        Controls.Add(_statusLabel);
        Controls.Add(toolStrip);

        _topmostTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _topmostTimer.Tick += (_, _) => ReassertTopmost();
        _topmostTimer.Start();

        _saveDebounceTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _saveDebounceTimer.Tick += (_, _) => { _saveDebounceTimer.Stop(); SaveWindowPosition(); };

        Move += (_, _) => _saveDebounceTimer.Stop();
        Move += (_, _) => _saveDebounceTimer.Start();
        Resize += (_, _) => _saveDebounceTimer.Stop();
        Resize += (_, _) => _saveDebounceTimer.Start();
        FormClosing += (_, _) => SaveWindowPosition();

        RebuildTiles();
    }

    /// <summary>Rebuilds the tile grid from the current bank contents.</summary>
    public void RebuildTiles()
    {
        foreach (Control control in _tilePanel.Controls)
        {
            if (control is Panel tile)
            {
                foreach (Control child in tile.Controls)
                {
                    if (child is AnimatedGifBox box)
                    {
                        box.StopAnimating();
                    }
                }
            }
        }
        _tilePanel.Controls.Clear();

        foreach (var entry in _bank.Entries)
        {
            _tilePanel.Controls.Add(BuildTile(entry));
        }
    }

    /// <summary>Updates the status label, e.g. "Popup open: Mind Blown" / "Ready".</summary>
    public void SetStatus(string text)
    {
        _statusLabel.Text = text;
    }

    private Control BuildTile(GifEntry entry)
    {
        const int tileSize = 120;

        var tile = new Panel
        {
            Width = tileSize,
            Height = tileSize + 36,
            Margin = new Padding(6),
            BorderStyle = BorderStyle.FixedSingle,
            Enabled = entry.Hotkey is not null,
        };

        var preview = new AnimatedGifBox
        {
            Width = tileSize - 4,
            Height = tileSize - 4,
            Left = 2,
            Top = 2,
            Image = entry.Image,
        };
        preview.StartAnimating();

        var badge = new Label
        {
            Text = entry.Hotkey?.ToString() ?? "-",
            AutoSize = false,
            Width = 24,
            Height = 24,
            Left = 2,
            Top = 2,
            BackColor = Color.FromArgb(200, 0, 0, 0),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font, FontStyle.Bold),
        };

        var titleLabel = new Label
        {
            Text = entry.Title,
            AutoSize = false,
            Width = tileSize - 4,
            Height = 32,
            Left = 2,
            Top = tileSize,
            TextAlign = ContentAlignment.MiddleCenter,
        };

        if (entry.Hotkey is null)
        {
            titleLabel.Text += " (unassigned)";
        }

        tile.Controls.Add(preview);
        tile.Controls.Add(badge);
        tile.Controls.Add(titleLabel);
        badge.BringToFront(); // badge must render on top of the overlapping preview
        return tile;
    }

    private void OpenGifFolder()
    {
        var folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _config.GifsFolder));
        Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start("explorer.exe", folder);
    }

    private void SaveWindowPosition()
    {
        _config.ControlWindow.X = Location.X;
        _config.ControlWindow.Y = Location.Y;
        _config.ControlWindow.Width = Size.Width;
        _config.ControlWindow.Height = Size.Height;
        _config.Save();
    }

    private void ReassertTopmost()
    {
        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }
}
