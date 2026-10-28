using System.Windows.Forms;

namespace GifPopup;

/// <summary>One GIF in the bank: its file, display title, assigned hotkey and loaded image.</summary>
public sealed class GifEntry
{
    public required string FilePath { get; init; }
    public required string Title { get; init; }
    public char? Hotkey { get; set; }
    public required Image Image { get; init; }
}

/// <summary>
/// Scans the configured GIF folder and builds the ordered bank of
/// <see cref="GifEntry"/> items, assigning hotkeys A-Z (overridable per
/// filename via config, capped at 26 assigned keys).
/// </summary>
public sealed class GifBank : IDisposable
{
    private const int MaxHotkeys = 26;

    private readonly AppConfig _config;
    private List<GifEntry> _entries = new();

    public IReadOnlyList<GifEntry> Entries => _entries;

    public GifBank(AppConfig config)
    {
        _config = config;
        Rescan();
    }

    /// <summary>
    /// Re-scans the GIF folder from scratch, disposing any previously loaded
    /// images first so file handles/memory from the old scan aren't leaked.
    /// </summary>
    public void Rescan()
    {
        foreach (var entry in _entries)
        {
            entry.Image.Dispose();
        }
        _entries = new List<GifEntry>();

        var folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _config.GifsFolder));
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
            return;
        }

        var files = Directory.GetFiles(folder, "*.gif")
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Pass 1: honour overrides so a specific GIF keeps its key even as
        // new files shift the alphabetical order.
        var usedLetters = new HashSet<char>();
        var overrideByFile = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase);
        foreach (var (fileName, letter) in _config.HotkeyOverrides)
        {
            if (letter.Length == 1 && char.IsLetter(letter[0]))
            {
                var upper = char.ToUpperInvariant(letter[0]);
                overrideByFile[fileName] = upper;
            }
        }

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var title = BuildTitle(fileName);
            var image = LoadImageWithoutLockingFile(file);

            char? hotkey = null;
            if (overrideByFile.TryGetValue(fileName, out var overrideLetter) && !usedLetters.Contains(overrideLetter))
            {
                hotkey = overrideLetter;
                usedLetters.Add(overrideLetter);
            }

            _entries.Add(new GifEntry
            {
                FilePath = file,
                Title = title,
                Hotkey = hotkey,
                Image = image,
            });
        }

        // Pass 2: fill remaining entries with the next free letter A-Z, in
        // display order, until the 26-key cap is reached.
        char nextLetter = 'A';
        foreach (var entry in _entries)
        {
            if (entry.Hotkey is not null)
            {
                continue;
            }

            while (usedLetters.Contains(nextLetter) && nextLetter <= 'Z')
            {
                nextLetter++;
            }

            if (nextLetter > 'Z' || usedLetters.Count >= MaxHotkeys)
            {
                break; // 27th+ GIF stays unassigned — shown disabled in the UI.
            }

            entry.Hotkey = nextLetter;
            usedLetters.Add(nextLetter);
            nextLetter++;
        }
    }

    /// <summary>Looks up an entry by its assigned hotkey, if any.</summary>
    public GifEntry? FindByHotkey(Keys key)
    {
        if (key < Keys.A || key > Keys.Z)
        {
            return null;
        }

        var letter = (char)('A' + (key - Keys.A));
        return _entries.FirstOrDefault(e => e.Hotkey == letter);
    }

    private static string BuildTitle(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        name = name.Replace('_', ' ').Replace('-', ' ');
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
    }

    /// <summary>
    /// Loads a GIF into memory via a MemoryStream copy rather than directly
    /// from the file path, so the file isn't locked while the app runs and
    /// can be replaced/edited externally.
    /// </summary>
    private static Image LoadImageWithoutLockingFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var stream = new MemoryStream(bytes);
        return Image.FromStream(stream);
    }

    public void Dispose()
    {
        foreach (var entry in _entries)
        {
            entry.Image.Dispose();
        }
        _entries.Clear();
    }
}
