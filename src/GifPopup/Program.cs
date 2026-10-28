using System.Windows.Forms;

namespace GifPopup;

static class Program
{
    private static GifBank? _bank;
    private static ControlForm? _controlForm;
    private static PopupForm? _popupForm;
    private static GlobalKeyHook? _hook;
    private static bool _popupOpen;

    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        var config = AppConfig.Load();
        _bank = new GifBank(config);
        _popupForm = new PopupForm();
        _controlForm = new ControlForm(config, _bank);
        _hook = new GlobalKeyHook();

        _controlForm.RescanRequested += () =>
        {
            _bank.Rescan();
            _controlForm.RebuildTiles();
            RearmHotkeys(config);
        };

        _hook.KeyPressed += key => OnHotkeyPressed(key, config);
        RearmHotkeys(config);
        _hook.Start();

        _controlForm.FormClosing += (_, _) => _hook.Dispose();

        Application.Run(_controlForm);
    }

    private static void RearmHotkeys(AppConfig config)
    {
        var keys = _bank!.Entries
            .Where(e => e.Hotkey is not null)
            .Select(e => (Keys)Enum.Parse(typeof(Keys), e.Hotkey!.Value.ToString()))
            .Append(Keys.Space)
            .ToList();
        _hook!.SetArmedKeys(keys);
    }

    private static void OnHotkeyPressed(Keys key, AppConfig config)
    {
        if (_controlForm!.InvokeRequired)
        {
            _controlForm.BeginInvoke(() => OnHotkeyPressed(key, config));
            return;
        }

        if (key == Keys.Space)
        {
            if (_popupOpen)
            {
                _popupForm!.HideAndStop();
                _popupOpen = false;
                _controlForm.SetStatus("Ready");
            }
            return;
        }

        if (_popupOpen)
        {
            // A popup is already open — ignore other hotkeys until Space closes it.
            return;
        }

        var entry = _bank!.FindByHotkey(key);
        if (entry is null)
        {
            return;
        }

        var screen = ResolveTargetScreen(config);
        _popupForm!.ShowGif(entry, screen, config.PopupSizeMode, config.PopupFixedWidth, config.PopupFixedHeight);
        _popupOpen = true;
        _controlForm.SetStatus($"Popup open: {entry.Title}");
    }

    private static Screen ResolveTargetScreen(AppConfig config)
    {
        var screens = Screen.AllScreens;
        if (config.PopupMonitorIndex >= 0 && config.PopupMonitorIndex < screens.Length)
        {
            return screens[config.PopupMonitorIndex];
        }

        var nonPrimary = screens.FirstOrDefault(s => !s.Primary);
        return nonPrimary ?? Screen.PrimaryScreen!;
    }
}
