# vSuite design guide

How a vSuite app looks, behaves and is put together, taken from the apps as they are (vShell, vXplorer, vView, vTerm, vPad).
Follow it for a new app and for new parts of an existing one, so the suite stays one thing. When an app has to differ, write down why
in that app's CLAUDE.md. When the suite changes its mind, change this guide in the same commit.

Contents: [Principles](#1-principles) · [The window](#2-the-window) · [Colour and theme](#3-colour-and-theme) ·
[Type and glyphs](#4-type-and-glyphs) · [The app icon](#5-the-app-icon) · [Controls](#6-controls-and-interaction) ·
[Keys](#7-keys) · [Words](#8-words) · [Options](#9-options) · [Plumbing](#10-plumbing) · [Looking after data](#11-looking-after-data) ·
[Build, release and repo](#12-build-release-and-repo) · [New app checklist](#13-new-app-checklist)

---

## 1. Principles

1. **It wears vShell's theme.** Every colour, the corner radius, the opacity and the blur come from the user's vShell theme, and change
   the moment it changes. An app never brings colours of its own (syntax highlighting and status colours are worked out *from* the
   theme - see 3).
2. **It works without vShell.** vShell is the home, not a requirement: no vShell = vShell's default theme, and everything still works.
3. **One look, one way of doing things.** The same window frame, menu, tabs, Options, update notice and words in every app. If another
   app already has a thing, use the shared one (vSuite.Core) - don't make a second.
4. **Plain and friendly.** Short sentences, everyday words, no jargon in the UI. Say what happened and what to do next.
5. **Never lose anything.** Ask before work is lost, keep files exactly as they were stored (encoding, line endings), write safely
   (to a temp file, then swap), keep secrets encrypted.
6. **Quiet and quick.** No pop-ups that aren't needed; a note in the status line instead. Slow work off the window's thread. The log
   holds real problems only.
7. **Small and focused.** Each app does its one job well; the suite does the rest (vShell drops vTerm down, vPad opens vTerm's servers,
   vXplorer opens files in vView / vPad).
8. **Keeps itself up to date** from vsuite.vrsion.com, installs per user with no admin rights, and can be installed / updated from
   vShell's Settings > Updates > vSuite apps.

## 2. The window

Borderless WPF window with `WindowChrome` (`CaptionHeight` = the caption row, `ResizeBorderThickness` 6, no glass frame, no Aero
caption buttons), dressed with `WindowEffects.Look(this, theme, blur)` on every theme change. Implement `IThemedWindow` so the
library's dialogs take its theme.

**The caption row (40 px)**, left to right:

| Part | What | Shared piece |
|---|---|---|
| App icon (34 x 30-32) | The app's icon in the theme's colours, 18 px (`AppIcon.Image(theme, true)`), **its menu** | `vx.IconButton` |
| Side panel button | `\uE700`, only when the app has a side panel (Ctrl+B) | `vx.IconButton` |
| Tabs, then + | When the app has documents / places / shells | `TabStrip` (34 px tabs, Reserve 56-60) |
| Free caption | Drags the window, double-click maximises | WindowChrome |
| App switches | E.g. vPad's Text / Side by side / Preview, only when they apply | `vx.CommandButton` in a `vx.Ctrl` pill |
| Update | `\uE896` in the accent, only while an update is out and its notice is hidden | `vx.IconButton` |
| Keep on top | `\uE718` (`\uE77A` when on) - applies to every window of the app | |
| Minimise / maximise / close | `\uE921`, `\uE922` / `\uE923`, `\uE8BB` | `vx.CaptionButton`, `vx.CloseButton` |

Everything clickable in the caption row needs `WindowChrome.IsHitTestVisibleInChrome="True"` (the vx button styles set it).

**The menu (the app icon)**, in this order, each with its glyph and key: New … / New window / Open … / Open recent ▸ - separator -
the app's own commands - separator - Options / Check for updates / About <App> - separator - Close window (Alt+F4).

**Body.** Optional side panel on the left (`vx.Panel` background, a `GridSplitter` with a 1 px `vx.Line`, its width remembered, its own
tabs as in vPad: Files / Servers / Search). The content takes the rest.

**Status line (26 px, optional).** Top border `vx.Line`, text in `vx.FgDim` at 12 px: position / facts on the left, a short note
("Saved at 3:42 PM") and the kind of thing on the right. Items you can change are `vx.CommandButton`s opening a menu **upwards**.

**Edges.** The window resizes from a 6 px band at its edges - that band takes the mouse. When not maximised, keep anything you grab
(scrollbars especially) out of it: put the content 6 px in from the right edge (vPad, Options). Maximised: pad the root by
`WindowEffects.MaximizedOverhang` so nothing hangs off the screen.

**Placement.** Remember position, size and maximised; on start put it back if it's still on a screen, else centre it. A second
window opens 32 px down-right of the one in front. Remember per app, not per window.

**Native content** (WebView2, VLC, a terminal's text window) draws over WPF - put notices over it in a `CornerWindow` (the update
notice: `NoticeWindow`) and keep it off the resize edges.

## 3. Colour and theme

- Read the theme with `VShellThemeSource` (its `Changed` event: apply again), `Theme.From(source.Current)`, then
  `theme.Apply(Application.Current.Resources, seeThrough)` (vShell: per window via `Look.Dress` - never app-wide).
- **Only `vx.*` brushes, always as DynamicResource / `SetResourceReference`.** Never a colour literal in the UI.

| Key | Use |
|---|---|
| `vx.Bg` | Window background (theme colour at its opacity over the glass) |
| `vx.Fg` / `vx.FgDim` | Text / secondary text, hints, line numbers, status line |
| `vx.Accent` / `vx.OnAccent` | Main Accent: the chosen tab's line, a primary button, the caret, links / text on an accent fill |
| `vx.Secondary` | Secondary Accent: highlights that must differ from the accent (search matches, variables) |
| `vx.Panel` / `vx.PanelBorder` | Cards, side panels, the chosen tab / their outline |
| `vx.Hover` / `vx.Active` / `vx.Selected` | Pointer over / pressed or drop target / selected item |
| `vx.Line` | Dividers |
| `vx.Popup` | Menus, notices, floating bars (find) |
| `vx.Ctrl`, `vx.CtrlHover`, `vx.CtrlBorder`, `vx.SelBg` | Text boxes and other form controls |
| `vx.Track` / `vx.Thumb` | Scrollbars, sliders |
| `vx.Radius`, `vx.ItemRadius`, `vx.MenuRadius`, `vx.TabRadius` | Corners (follow the theme's radius) |

- **Glass:** Blur / Frosted = the blurred wallpaper under `vx.Bg` (`Glass`, via `WindowEffects.Look`) - **never Windows' Mica or
  Acrylic**. Options > Look > "Use the theme's opacity" (on by default) switches to solid.
- **Colours you have to make** (syntax, status, charts): start from the theme - keys and keywords in the Main Accent, the second kind in
  the Secondary Accent, comments and punctuation in dimmed text; green / orange / yellow for strings / numbers / types, lighter on a dark
  theme and darker on a light one (vPad's `Syntax.Paint`). A colour too close to the background is lightened or darkened to read.
- Text on the theme's background must stay readable: the theme fixes the text colour if it's too close; do the same for anything you mix.

## 4. Type and glyphs

- **Segoe UI**, 12.5 px for the window, 12 px for status and hints, 11.5 px for small print. Headings: 15 (dialog titles), 17 (Options'
  app name), page titles as OptionsWindowBase draws them. SemiBold for emphasis, never Bold in the UI.
- **Monospace: Consolas** by default (WPF draws Windows 11's variable Cascadia fonts too heavy); let the user pick another in Options.
- **Glyphs: Segoe Fluent Icons** (`vx.Icons`), 10-13 px; in menus with `vx.MenuGlyph`. The ones the apps use:

| Glyph | For | Glyph | For |
|---|---|---|---|
| `\uE700` | Side panel | `\uE710` | New / add |
| `\uE8BB` | Close | `\uE921` `\uE922` `\uE923` | Minimise, maximise, restore |
| `\uE718` / `\uE77A` | Keep on top / on | `\uE896` | Update available |
| `\uE713` | Options | `\uE895` | Check for updates |
| `\uE946` | About | `\uE8E5` | Open |
| `\uE74E` / `\uE792` | Save / Save as | `\uE721` | Find |
| `\uE8A7` | New window / move to a window | `\uE8C8` | Copy |
| `\uE838` | Show in its folder | `\uE72C` | Refresh / read again |
| `\uE8B7` | Folder | `\uE76B` `\uE76C` | Scroll left / right |

- No emoji in the UI.

## 5. The app icon

Every app's icon is drawn in code (`Brand.Draw`, set as `AppIcon.Drawer`) on a **64 x 64 grid**, so it can be painted in the theme's
colours at run time (`WindowIcons` for the taskbar / Alt+Tab, `AppIcon.Image` in the app) - and in vShell's default colours for the
exe (`<App>.exe --make-icon <app>.ico`, run again whenever the drawing changes).

- **An outline shape** that says what the app is (vXplorer: a folder, vView: a play shape, vTerm: a window, vPad: a spiral notepad),
  stroked 4-4.2 px in the **Main Accent**, filled with the theme's **background**.
- **Inside, in the text colour**, the simplest sign of the job (a prompt `>`, two lines of text).
- **vShell's three dots** - Main Accent, Secondary Accent, text colour, radius ~3 - always there, usually as the "cursor" or the last
  line. They're what makes it a vSuite icon.
- **A small version** (`small` = 16-24 px): heavier lines (5-5.5 px), bigger dots, fewer details (two rings instead of three, no fold).
- Check it at 16, 32 and 256 px before settling, and show the choices to V as mock-ups first.

## 6. Controls and interaction

- **Styles:** `Controls.xaml` (app-wide, after `vSuite.App.Init`) gives every style: `vx.IconButton`, `vx.TextButton`,
  `vx.CommandButton`, `vx.CaptionButton`, `vx.CloseButton`, `vx.TabClose`, `vx.ScrollArrow`, menus, tooltips, trees, lists and the
  scrollbars. `Forms.xaml` (buttons, switches, sliders, drop-downs, text boxes) is for Options-type windows only (`Look.Dress(…, forms: true)`).
  Don't restyle these locally; if one is wrong, fix it in the library.
- **Scrollbars:** never a style of your own - the library's (12 px to aim at, a 6 px thumb that widens to 10 px when pointed at, never
  shorter than 40 px). A web view (WebView2) uses the same in CSS (vTerm's `term.html`).
- **Tabs:** `TabStrip` - shared width 90-220 px, arrows when they don't fit, the wheel scrolls them, drag along to reorder (the others
  slide aside), pull out for a new window, drop on another window to move, a window's only tab moves the window, middle-click closes,
  right-click: the tab's menu (Close / Close the others / Close the ones to the right / Move to a new window / Move to the window with … /
  Copy the path / Show in its folder). Unsaved: `StripTab.Dirty`.
- **Questions:** `Ask.YesNo` / `Ask.Show` (themed; Enter = the default, Esc = cancel; `danger` for destructive ones) - **never
  `MessageBox`**. Unsaved work: "Save the changes to <name>?" - Save / Don't save / Cancel.
- **Small inputs:** `FormWindow` (`FormWindow.Prompt`, `FormField.Password` …) - a password, a line number, a name.
- **Menus** open at the pointer (right-click) or under their button; status line menus open upwards. Ticks for on / off choices.
- **The tray** (notification area): only when the app runs in the background (vShell's own icon, vTrace's "Minimise to the tray" - an
  option, off at first, on the Look page). Always `vSuite.Ui.TrayIcon` - never Windows Forms' NotifyIcon: click = bring the window back
  (or the app's main action), right-click = a short menu (Open <app>, its one or two main actions, Options, then a separator and
  "Close <app>"), dressed in the suite's look with the theme (`MenuTheme`), closing on a click elsewhere / Esc. Tooltip: the app's name
  and its one line of news ("vTrace - 12 changes today"). A window hidden in the tray calls `Instance.InTray(hwnd, true)` so a second
  start brings it back.
- **Drag and drop:** files dropped on the window open; a folder dropped shows it. Drags start after the system's drag distance.
- **Hover** shows a tooltip on every icon-only button: what it does and its key ("New file (Ctrl+N)").
- **Busy:** a note in the status line ("Opening compose.yaml from docker-vm…"), never a frozen window; long work with `async` off the
  window's thread.
- **Notices** (an update): `UpdateNotice` in the window's corner (or `NoticeWindow` over native content) - never a Windows toast.

## 7. Keys

| Key | Does | Key | Does |
|---|---|---|---|
| Ctrl+N | New (file / tab of the default kind) | Ctrl+Shift+N | New window |
| Ctrl+O | Open | Ctrl+S / Ctrl+Shift+S | Save / Save as |
| Ctrl+W (Ctrl+F4) | Close the tab | Ctrl+Shift+T | Reopen the last closed tab |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab | Ctrl+PageDown / PageUp | Next / previous tab |
| Ctrl+Shift+O | Move the tab to a new window | Ctrl+B | Side panel |
| Ctrl+F / Ctrl+H | Find / Replace | F3 / Shift+F3 | Next / previous match |
| Ctrl+G | Go to (line) | Ctrl + wheel, Ctrl+= / Ctrl+- / Ctrl+0 | Bigger / smaller / back to normal |
| Esc | Closes the floating thing (find, a drag, a dialog) | Alt+F4 | Close the window |

A terminal app keeps Ctrl+<letter> for the shell: vTerm uses Ctrl+Shift+<letter> for its own (Ctrl+Shift+T / W / O).

## 8. Words

- **Plain, friendly English**, sentence case, no full stop on single phrases. "Read it again", not "Reload"; "Show in its folder",
  not "Reveal in Explorer"; "Couldn't save docker-compose.yml: <reason>", not "Error 0x80070005".
- The app's settings window is **Options** (vShell's is Settings). Its pages are nouns: General, Text, Look, Connections, Updates, About.
- A setting is a short statement of what's on ("Wrap long lines", "Keep on top of other windows"), with a hint underneath saying what it
  does or where else it is ("Also Alt+Z"), and an (i) for the longer story.
- **Times are 12-hour** ("3:42 PM", `DateTime.ToString("h:mm tt")` or `TimeText.Stamp`); sizes as "4.2 MB".
- Errors say what happened, then what to do: "The server won't let root write this file. Save a copy, or change it in vTerm with sudo."
- Name things as the user sees them: "Servers" (vPad's), "saved connections", "vTerm's connections" - not "SFTP endpoints".
- Tooltips end with the key in brackets: "Side panel (Ctrl+B)".
- **CHANGELOG lines** are one short line per change, what not how: "The scrollbar is easier to grab." Details go in TESTING.md.

## 9. Options

`OptionsWindowBase` (the suite's Options window: a page list on the left, pages of cards on the right, every change saved and applied
at once - **no OK button**, one Options window at a time, Esc closes).

- **Pages, in this order:** General (starting, defaults) · the app's own (Text, Connections, Jellyfin …) · **Look** · **Updates**
  (`UpdatesPage(settings)` - always) · **About** (always).
- **Look** always has: "Use the theme's opacity" (see-through like the bars - off: solid) and "Keep on top of other windows", and ends
  with the note "Colours, opacity, blur and corners come from your vShell theme - change them in vShell's settings."
- **About** always has: "<App> x.y.z" and one sentence on what it is; Colours ("Your vShell theme - <App> follows it as you change
  it" / "vShell's default theme"); Settings folder (Open); "Made with" + the licences of what it uses.
- Rows: `Switch` for on / off, `Segmented` for 2-4 choices, `Combo` for more, `SliderBox` for numbers, `TextField` for text.

## 10. Plumbing

- **Start** (`Program.Main`): `vSuite.App.Init("<App>", "<PREFIX>", "https://vsuite.vrsion.com")` first, `AppIcon.Drawer = Brand.Draw`;
  `--make-icon`, `--update-watchdog` (`Updater.RunWatchdog`); a `Mutex("<App>.Running")` for the installer; one copy per settings folder
  (`Instance.HandOff(...)` - a second start hands its arguments to the running one, which takes them in `WM_COPYDATA` via
  `Instance.Received` and comes to the front); unhandled-exception handlers that log and `Log.Flush()`; merge `Controls.xaml`;
  `UpdateCenter.Start(settings)` once the first window has rendered; `--update` (vShell's Update button) opens a window in front and
  updates.
- **Settings:** `%APPDATA%\<App>\settings.json` (`<PREFIX>_DATA_DIR` for tests) - an `AppSettings` class with `Load` (falls back to
  defaults), `Normalize` (puts bad / old values right), `Save` (to `.tmp`, then moved over) and `SaveSoon`; implements
  `IUpdateSettings`. Never hold settings in the registry.
- **Log:** `Log.Write` (`%APPDATA%\<App>\<app>.log`, rotates at 1 MB). One line per event; errors with their reason; routine noise
  (a library's chatter, an expected cancel) left out - observe the tasks you cancel.
- **Another app's data** is read, never written (vPad reads vTerm's connections through `VTermConnections`); talk to a running app
  through its own channel (vShell → vTerm: `TermLink`; vShell → any app: `--update`, `--update-pre`).
- **vShell knows the app:** add it to vShell's Settings > Updates > vSuite apps (`SettingsWindow.Suite.cs`: name, glyph, one-line blurb,
  the installer's AppId).
- **Threads:** network and disk off the window's thread (`await`, and `ConfigureAwait(false)` in library code); progress marshalled
  back with `Dispatcher.BeginInvoke` and reported in steps (each 0.1 %), not per chunk.
- **Tests:** `tests\<App>.Tests` (xunit) for everything without a window (formats, parsing, rules); `<PREFIX>_TEST_HOOKS=1` test
  commands by WM_COPYDATA (`scripts\test-send.ps1`) for the windows - tests never move the real mouse or keyboard.

## 11. Looking after data

- **Files:** keep their encoding, BOM and line endings; write to a temp file beside them and swap it in; a read-only file is refused
  with a message, never forced. Notice changes made elsewhere: read again if there are no changes here, else ask.
- **Unsaved work** is never lost without a question (closing a tab, a window, updating).
- **Secrets** (passwords, tokens, passphrases) only through `Secret.Protect` / `Unprotect` (DPAPI, the "dpapi:" form every app reads).
- **Servers:** trust a server's key once (asked, with its fingerprint), warn loudly if it changes; no passwords in logs.
- **Updates** are checked against the release's SHA-256, and the watchdog puts the old version back if the new one won't start.

## 12. Build, release and repo

- **Repo** (`https://github.com/version-au/<App>`, branch `main`, GPL-3.0; vSuite.Core is MIT): `<App>.csproj` (.NET 10 WPF,
  `Version` 0.1.N, `ApplicationIcon`), `app.manifest` (PerMonitorV2, long paths), `src\Core` (no WPF types) and `src\Ui`, `tests\<App>.Tests`,
  `installer\<App>.iss`, `build.cmd`, `scripts\release-notes.ps1`, `vSuite.Core` (git submodule), `README.md`, `CLAUDE.md`, `CHANGELOG.md`,
  `TESTING.md`. Copy the newest app's (vPad's) and rename.
- **build.cmd** runs `vSuite.Core\scripts\suite-core.ps1` first (the newest library), publishes one self-contained exe, makes the
  installer (Inno Setup: per user, `%LOCALAPPDATA%\Programs\<App>`, a new AppId GUID, `AppMutex=<App>.Running`, `<App>-setup.exe` - the
  same name every release, uninstall offers to delete the settings folder), the release notes from CHANGELOG.md and the update site's
  files (`site-release.ps1` - it also marks the version's heading in CHANGELOG.md "· pre-release" or "· release": commit CHANGELOG.md after every build); `upload` puts them on the site, `pre` makes a pre-release. **Only when V asks.**
- **Code signing** (1.0.50): the Certum open-source certificate, in Certum's cloud - **SimplySign Desktop must be connected** (a code
  from the phone) before a build. build.cmd signs the app straight after publishing (`vSuite.Core\scripts\sign.ps1`: SHA-256, Certum's
  timestamp, verified), Inno Setup signs the setup and its uninstaller (`SignTool=vsuite` under `#ifdef SIGN` in the .iss; build.cmd
  passes `/DSIGN "/Svsuite=$q...\sign.cmd$q $f"`), and site-release.ps1's hashes are of the signed files. Not connected: a local build
  goes on unsigned (sign.ps1 exit 2); `upload` stops. `build.cmd nosign` skips it (never uploaded). vView signs its own exe / DLLs
  before zipping (not VLC's or .NET's). The certificate is found by issuer + Code Signing (or `%VSUITE_SIGN_THUMBPRINT%`) - nothing
  about it is in the repos.
- **House rules:** every user-visible change bumps the version (the next N once the current one has been built), with one CHANGELOG line
  and test steps in TESTING.md; commit messages `0.1.N: what changed` in lower case; shared pieces change in vSuite.Core first (with its
  tests), then each app moves to it.
- **The website** (vSuite.Site): a section per app - tag line, icon, one-sentence lead, five features (a tour of screenshots for the main
  ones), Download and What's new - plus the nav bar, the downloads list and the footer. Screenshots from made-up demo data, never V's own
  files; blur user names and addresses.

## 13. New app checklist

- [ ] Repo from the newest app; `vSuite.Core` submodule; names, PREFIX, AppId GUID, mutex changed.
- [ ] `App.Init`, single instance + hand-off, settings with `Normalize` / atomic `Save`, log, unhandled handlers.
- [ ] Icon: mock-ups (A / B / C) shown to V, then `Brand.Draw` + small version + `--make-icon` .ico.
- [ ] Window: WindowChrome, caption row (icon menu, tabs via `TabStrip` if it has documents, pin / min / max / close), status line if it
      has facts to show, content off the resize edges, placement remembered.
- [ ] Theme: `vx.*` only, `VShellThemeSource.Changed` applied live, glass via `WindowEffects.Look`, "Use the theme's opacity".
- [ ] Menu: New / Open / … / Options / Check for updates / About / Close window, with glyphs and keys.
- [ ] Options: General · own pages · Look · Updates · About, as section 9.
- [ ] Questions with `Ask`, inputs with `FormWindow`, no `MessageBox`; unsaved work asked about.
- [ ] Keys from section 7; tooltips with keys; words from section 8; 12-hour times.
- [ ] Updates: `UpdateCenter.Start`, the notice placed (in a corner window over native content), `--update` works from vShell.
- [ ] vShell's Settings > Updates > vSuite apps lists it.
- [ ] Tests (`tests\<App>.Tests`) for the window-less parts; test hooks for the windows.
- [ ] build.cmd / installer / site files; CLAUDE.md (what, layout, things learned), README, CHANGELOG `## 0.1.0`, TESTING.
- [ ] Website section with demo-data screenshots, after V has seen it locally.
