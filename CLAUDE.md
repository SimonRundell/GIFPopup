# GifPopup — Development Instructions for Claude Code

Read [DESIGN.md](DESIGN.md) first — it is the source of truth for behaviour and architecture. This file is the build plan and project-specific conventions.

## Stack

- .NET 8, C# 12, Windows Forms (not WPF, not MAUI).
- No third-party NuGet packages unless genuinely necessary — this is a small single-purpose tool. `System.Drawing.ImageAnimator` covers GIF animation; no need for a GIF library.
- Single solution `GifPopup.sln`, single project `src/GifPopup/GifPopup.csproj` (`OutputType=WinExe`, `UseWindowsForms=true`, `TargetFramework=net8.0-windows`).
- JSDocs/PHPDocs-style inline documentation convention doesn't apply here (C#, not JS/PHP) — use standard `///` XML doc comments on public classes/methods instead.

## Build order

Work through these in order; each should build and run before moving to the next.

1. **Scaffold** — `dotnet new winforms` into `src/GifPopup`, add to a new `GifPopup.sln`. Verify it builds and an empty form launches.
2. **`NativeMethods.cs`** — P/Invoke declarations only, grouped together: `SetWindowsHookEx`, `UnhookWindowsHookEx`, `CallNextHookEx`, the `LowLevelKeyboardProc` delegate and `KBDLLHOOKSTRUCT`, `SetWindowPos` + `HWND_TOPMOST`/`SWP_NOMOVE`/`SWP_NOSIZE` constants, `GetWindowLong`/`SetWindowLong` + `GWL_EXSTYLE`/`WS_EX_LAYERED`/`WS_EX_TRANSPARENT`.
3. **`GlobalKeyHook.cs`** — wraps the hook lifecycle (`Start()`/`Stop()`, `IDisposable`), exposes a C# event `KeyPressed(Keys key)` that fires for keys it is told to watch, and *only swallows* the keys it's told to swallow (pass the "currently armed" hotkey set + Space in; everything else must call `CallNextHookEx` and return immediately). Unit-testable logic (matching a key against a set) should be separated from the raw hook plumbing so it isn't tied to an actual OS hook in tests.
4. **`AppConfig.cs`** — POCO + `Load()`/`Save()` using `System.Text.Json`, default values matching DESIGN.md §4.1, written to `config.json` next to the exe. Create the file with defaults if missing. Keep this tolerant of a missing/corrupt file (fall back to defaults, don't crash).
5. **`GifBank.cs`** — scans `gifsFolder`, builds the ordered list of `GifEntry { string FilePath; string Title; char? Hotkey; Image Image; }`, applies `hotkeyOverrides` before falling back to alphabetical A–Z assignment, caps at 26 assigned hotkeys. Expose a `Rescan()` method that can be called again at runtime (dispose old `Image` instances properly before reloading — GIF `Image` objects hold file handles/memory, don't leak on rescan).
6. **`AnimatedGifBox.cs`** — small `PictureBox` subclass wiring up `ImageAnimator.Animate`/`StopAnimate` safely (handle being set to a non-animated or null image without throwing).
7. **`PopupForm.cs`** — borderless, topmost, click-through (apply the extended styles in `OnLoad`, after `Handle` exists), positioned/sized per `popupSizeMode` on the configured `Screen`. Exposes `ShowGif(GifEntry)` / `HideAndStop()`. Re-assert topmost on a timer while visible.
8. **`ControlForm.cs`** — tile grid, letter badges, rescan/monitor/size controls, position+size persistence (debounce `Move`/`Resize` writes, e.g. a 500 ms idle timer, so config.json isn't rewritten on every pixel of a drag), status label. Re-assert topmost on a timer like `PopupForm`.
9. **`Program.cs`** — wires it together: load config → build `GifBank` → create `ControlForm` + `PopupForm` (popup hidden initially) → create `GlobalKeyHook`, subscribe: on a mapped letter with no popup open, show that GIF; on Space with popup open, hide it → `Application.Run(controlForm)` → dispose hook on exit.

## Things to get right (these are the parts most likely to go wrong)

- **Hook must be low-level and system-wide (`WH_KEYBOARD_LL`), not a WinForms `KeyDown` handler.** A normal `KeyDown` handler only fires when the form has focus, which breaks the "arrow keys always reach PowerPoint" requirement the moment the control panel happens to have focus. Confirm by testing with PowerPoint's Slide Show focused and the control panel *not* focused — letter hotkeys must still fire.
- **Only swallow the exact keys you're handling.** Returning a non-zero/non-`CallNextHookEx` result for *any* key you're not explicitly handling will eat keystrokes meant for other apps. Default path through the hook callback must always be `CallNextHookEx`.
- **Click-through styles must be set after the window handle exists** (`OnHandleCreated` or `OnLoad`, not the constructor) or `SetWindowLong` will silently no-op.
- **Topmost fights with PowerPoint's own full-screen Slide Show window.** Don't rely on setting `TopMost = true` once; re-assert periodically via `SetWindowPos`. Test this specifically with an actual PowerPoint slideshow running full-screen on the extended monitor, not just a normal window.
- **Dispose GIF `Image` objects on rescan/exit.** Loading GIFs via `Image.FromFile` on a path keeps a file lock unless the stream is copied into memory first (load via a `MemoryStream` copy, not directly from the file path, so the GIF files aren't locked while the app runs and can be edited/replaced externally).
- **Screen enumeration can change between sessions** (laptop docked vs undocked, monitor unplugged). Guard `popupMonitorIndex` against being out of range on load — fall back to "first non-primary screen, else primary" rather than crashing.
- **No admin rights required** for `WH_KEYBOARD_LL` — don't add a manifest requesting elevation.

## Manual test checklist before calling a milestone done

1. Launch with PowerPoint Presenter View open on the primary screen and a Slide Show full-screen on the extended screen.
2. Press a mapped letter with PowerPoint focused → popup appears on the extended screen over the slideshow, looping.
3. Press the left/right arrow keys → slide actually advances in PowerPoint, popup keeps animating, unaffected.
4. Click PowerPoint's on-screen "Advance" control → slide advances, popup unaffected.
5. Press Space → popup disappears.
6. Resize/move the control panel, close the app, relaunch → position/size restored from `config.json`.
7. Add a new GIF file to the `gifs` folder, click "Rescan" → it appears with the next free letter, without restarting the app.
8. Remove more than 26 GIFs' worth of files → confirm the 27th+ are visibly disabled rather than crashing or silently double-assigning a letter.

## Conventions

- `///` XML doc comments on all public types/members.
- No external GIF/animation NuGet packages (see Stack above) unless you hit a concrete limitation of `ImageAnimator` — note it in a comment if so, rather than swapping libraries silently.
- Keep `NativeMethods.cs` as the *only* file with P/Invoke/unsafe interop so Win32 details don't leak across the codebase.
- Commit messages: plain, factual, no AI self-attribution (per user's global git convention).
