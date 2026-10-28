# GifPopup — Design Document

## 1. Purpose

A small Windows Forms utility for use during live PowerPoint presentations on a two-monitor setup. The teacher runs PowerPoint Presenter View on the primary screen, with this app's control panel floating on top of it. Pressing an assigned letter key pops up a looping animated GIF full-screen (or windowed, size configurable) on the extended screen, where the audience-facing PowerPoint Slide Show is running. Spacebar dismisses the popup. PowerPoint keeps working normally throughout — slide advance via mouse click or arrow keys is never intercepted.

## 2. Target environment

- Windows 10/11, dual monitor (primary = teacher/presenter, secondary/extended = audience/projector).
- .NET 8, C# 12, Windows Forms.
- Visual Studio (solution + project created via VS or `dotnet new`).
- No admin rights required.

## 3. High-level architecture

Two forms, one hidden helper:

| Component | Screen | Role |
|---|---|---|
| `ControlForm` | Primary | Grid of GIF tiles (animated thumbnail + letter badge), always-on-top, draggable/resizable, sits over Presenter View |
| `PopupForm` | Secondary (extended) | Full-bleed or sized popup showing the selected GIF, click-through, always-on-top, closed by Space |
| `GlobalKeyHook` | n/a (system-wide) | Low-level keyboard hook that intercepts only the hotkeys this app cares about; everything else (including arrow keys) passes straight through to whatever window actually has focus (PowerPoint) |

### 3.1 Why a global low-level keyboard hook

The requirement "letter key triggers popup even though PowerPoint keeps receiving arrow keys to advance slides" is the trickiest constraint. The clean solution is **not** to fight for focus. Instead:

- Install a `WH_KEYBOARD_LL` hook (`SetWindowsHookEx`, no admin rights needed) for the life of the app.
- In the hook callback, check the key against the current GIF-hotkey map and the Space key:
  - If it matches a GIF hotkey **and** no popup is currently open → trigger that popup, **swallow** the keystroke (return 1 so Windows doesn't dispatch it further).
  - If it matches Space **and** a popup is open → close the popup, swallow the keystroke.
  - Everything else (arrows, Tab, Enter, mouse clicks, etc.) → return `CallNextHookEx` unmodified, so PowerPoint (or whatever has focus) behaves exactly as if GifPopup weren't running.
- This means the control panel never needs keyboard focus at all. The teacher can click "Advance Slide" in Presenter View, or use the arrow keys, with GifPopup's window sitting visually on top the whole time.
- Letter hotkeys work **globally**, not just "when the app is focused" — simpler and more reliable than trying to detect focus state, and matches the actual requirement once you consider PowerPoint full-screen slideshow aggressively grabs focus/topmost itself.

### 3.2 Keeping ControlForm on top of Presenter View

PowerPoint Presenter View is just a normal window, but a full-screen Slide Show window is itself topmost-like and can fight back. Mitigations:
- Set `ControlForm.TopMost = true`.
- On a `System.Windows.Forms.Timer` (~1s), re-assert via `SetWindowPos(hwnd, HWND_TOPMOST, …, SWP_NOMOVE | SWP_NOSIZE)` in case PowerPoint steals top-most ordering.
- Do the same re-assertion for `PopupForm` while it is visible.

### 3.3 Click-through popup

`PopupForm` must never intercept mouse input (so stray clicks on the projector screen don't do anything odd, and so it can't accidentally be dragged/closed by touch-screen projectors etc.):
- Extended window style: `WS_EX_LAYERED | WS_EX_TRANSPARENT` applied via `GetWindowLong`/`SetWindowLong` on `GWL_EXSTYLE` in `OnLoad`.
- Combined with `TopMost = true` and `FormBorderStyle = None`, `ShowInTaskbar = false`.
- This is strictly a visual overlay — it opens and closes only via the keyboard hook.

## 4. GIF bank

- A folder on disk (default: `.\gifs` next to the executable, overridable in config) is scanned at startup for `*.gif`.
- Sort order: alphabetical by filename, then letters `A`–`Z` are auto-assigned to the first 26 found. Order is also the display order in the control panel grid.
- Title shown under each tile = filename without extension, with underscores/hyphens turned into spaces (e.g. `mind_blown.gif` → "Mind Blown"). A per-file override is possible via an optional sidecar config (see below) but is not required for day-to-day use — just drop a GIF in the folder.
- Re-scanning: a "Rescan folder" button/menu item on `ControlForm` re-reads the folder without restarting the app (handles adding/removing GIFs between lessons).
- Maximum 26 active hotkeys (A–Z). If more than 26 GIFs exist, the 27th onward are shown in the grid but greyed out / unassigned with a tooltip explaining the limit.

### 4.1 Config file (`config.json`, next to the exe)

```json
{
  "gifsFolder": "./gifs",
  "popupMonitorIndex": 1,
  "popupSizeMode": "FullScreen",
  "controlWindow": { "x": 100, "y": 100, "width": 480, "height": 360 },
  "hotkeyOverrides": { "my_special_gif.gif": "Z" }
}
```

- `popupMonitorIndex`: index into `Screen.AllScreens`; defaults to the first non-primary screen detected at startup, but is stored so the choice is remembered if screen order changes.
- `popupSizeMode`: `FullScreen` (covers the whole extended display) or `Fixed` (a configurable width/height, centred on the extended display) — covers the case of wanting a smaller "sticker" style popup rather than full screen.
- `controlWindow`: persisted position/size of the control panel, since the user says this must be adjustable and presumably remembered between sessions.
- `hotkeyOverrides`: optional, keyed by filename, lets a specific GIF keep the same key even if new files are added and shift the alphabetical order.
- File is read on startup (created with defaults if missing) and rewritten whenever the control window moves/resizes or the user changes settings.

## 5. GIF rendering

Windows Forms has no native animated-GIF control. Approach:
- Use `PictureBox` + `System.Drawing.ImageAnimator` (`ImageAnimator.Animate(image, OnFrameChanged)`), which is the standard built-in way to animate a multi-frame GIF in WinForms without extra dependencies.
- Same mechanism is used both for the small looping preview thumbnails in `ControlForm` and for the large popup in `PopupForm` — one small reusable `AnimatedGifBox : PictureBox` control.
- Thumbnails: GIFs are loaded once at startup/rescan and kept in memory (bank is expected to be small — dozens of GIFs, not hundreds), scaled down for the tile view (`SizeMode = Zoom`).
- Popup: same `Image` instance reused (no need to reload from disk), scaled to fill the chosen popup size/monitor.
- Looping is automatic — GIFs with a loop-forever directive just keep animating via `ImageAnimator` for as long as the popup is open; this satisfies "looping, always" until Space closes it.

## 6. Control panel UI

- `FlowLayoutPanel` (or a simple custom-drawn grid) of tiles, one per GIF: animated thumbnail on top, letter badge (e.g. a circle with "A") overlaid or alongside, title underneath.
- Resizable form (`FormBorderStyle = Sizable`), draggable by its title bar as normal; size/position saved to `config.json` on `Resize`/`Move` (debounced) and on close.
- Status strip or small label at the bottom showing "Popup open: <name>" / "Ready" for the teacher's reassurance since they may not be looking at screen 2.
- Menu/button: "Rescan GIF folder", "Open GIF folder" (shell out to Explorer), "Choose popup monitor" (dropdown of detected screens), "Popup size: Full screen / Fixed", "Exit".

## 7. Popup behaviour summary

1. Teacher presses letter key `X` (anywhere, any app focused) → if `X` is mapped and no popup open → `PopupForm` is created/shown on the configured monitor, GIF `X` starts animating, loops forever.
2. Pressing a different mapped letter while a popup is already open: ignored (must close current one with Space first) — keeps behaviour predictable and avoids stacking popups. (Flag this choice to the user — alternative is "switch immediately to new GIF", easy to change later if preferred.)
3. Teacher presses Space → popup hides/disposes, GIF animation stops, hook goes back to listening for the next letter.
4. All other keys (arrows, Enter, Esc, F5, etc.) are never swallowed and reach PowerPoint/whatever has focus exactly as if GifPopup weren't running.

## 8. Project structure

```
GifPopup/
  GifPopup.sln
  src/
    GifPopup/
      GifPopup.csproj
      Program.cs
      ControlForm.cs / .Designer.cs / .resx
      PopupForm.cs / .Designer.cs
      AnimatedGifBox.cs
      GifBank.cs            // scans folder, builds hotkey map
      AppConfig.cs          // load/save config.json
      GlobalKeyHook.cs      // WH_KEYBOARD_LL wrapper
      NativeMethods.cs      // P/Invoke signatures (SetWindowsHookEx, SetWindowPos, window styles, etc.)
  gifs/                     // sample/default GIF bank, shipped empty or with a couple of placeholders
  config.json               // created on first run if absent
  DESIGN.md
  CLAUDE.md
  README.md
  LICENSE
```

## 9. Out of scope (v1)

- No in-app GIF import/rename/delete UI — manage the bank via the file system (simple, matches "drop a file in a folder" workflow).
- No audio.
- No networking.
- No support for more than one extended popup screen simultaneously.

## 10. Licensing

Released under Creative Commons Attribution-NonCommercial-ShareAlike 4.0: free to use, adapt and share non-commercially with attribution and under the same licence. Full terms: https://creativecommons.org/licenses/by-nc-sa/4.0/
