# vSuite.Core - notes for Claude Code

The library shared by V's (Steven's) suite: **vShell**, **vXplorer**, **vView**, **vTerm**, **vPad** and **vTrace** (each https://github.com/version-au/<App>), and the
server apps vDeploy and vPatch. Repo: https://github.com/version-au/vSuite.Core (branch `main`, MIT). This machine's notes (working folders, remotes):
`CLAUDE.local.md` (not in git).

Each app has this repo as a **git submodule** in its own folder (`<app>\vSuite.Core`) and a ProjectReference to the projects it uses,
so every app builds into its own exe as before and stays on the library version it was tested with. A fix lands here first; each
app then moves its submodule forward (`git -C vSuite.Core pull` + commit in the app) and bumps its own version in its own release.

## The design guide

**`docs\design-guide.md`** - how a vSuite app looks, behaves and is put together (the window, theme, icon, controls, keys, words, Options, plumbing, data, release) and a new-app checklist. Every app follows it; change it in the same commit when the suite changes its mind.

## Projects

- `vSuite.Core` (all three apps, no NuGet packages): namespaces `vSuite` (no window) and `vSuite.Ui`.
- `vSuite.Shell` (vXplorer, vView): `ShellIcons` - needs Vanara, which vShell doesn't use.
- `vSuite.Tests` (xunit): `dotnet test vSuite.Tests`. Run them after every change here.
- `vSuite.Server` (1.0.31, net10.0 + SSH.NET, no Windows): the **server apps** - vDeploy and vPatch,
  Linux web apps in an LXC / Docker. `ServerApp.Init(name, PREFIX, keyPrefix, extra executables)` names the app
  (PREFIX_DATA_DIR / _UPDATE_SITE / _LAUNCHER, /var/lib/<app>, <app>.log, id_<app>, the site folder <app>/). `Paths` / `JsonFile` / `Log`,
  `Password` / `Totp` / `IpRange` / `Secrets` ("vdk1:" AES-GCM with secret.key) / `Lockout`, `ApiKey` / `TrustedAddress` /
  `ISignInSettings`, `SshRunner` / `IHostRunner` / `HostProblem` / `SshKey` / `Sh.Q`, `RunResult` (plain .NET - vDeploy's trimmed helper
  links the file), `SelfUpdate` (`ISelfUpdateSettings`; run.sh swaps app.new in, exit code 3). Moved from vDeploy 0.1.17 unchanged in
  behaviour - vDeploy's tests (vDeploy.Tests) cover it for now. `IWebSettings` (ISignInSettings + Confirm, VShellColours, TextSize) is
  here too - an app's settings class lives in its project without web parts.
  `HostsFile` (1.0.35): the hosts file the server apps exchange ("vsuite-hosts" format 1 - name, address, port, SSH host key, OS,
  CPU, never a user / password / key; vDeploy's older "vdeploy-hosts" reads too) - `Json`, `Read` (problems in words), `FileName`, `Same`.
- `vSuite.Web` (1.0.32-1.0.35, Razor class library on vSuite.Server): the server apps' pages - a vSuite window in a browser.
  - `HostsCard` (1.0.35): Options > General > Hosts in vDeploy, vPatch and vTrace - export the ticked hosts (a data: link, no endpoint)
    and import a file; the app gives `ImportAction` (per host not here yet) and optionally `ImportAll`. Icons added: upload, pc, server,
    timeline, map, ai, outage.
  - Services: `WebTheme` (vShell's theme -> CSS variables; field names are what the vShell plugins send - keep them), `ThemeStore`
    (theme.json, `For` / `Css(settings)`), `Auth` + `Gate` (setup / cookie / trusted address / sign-in; an app key opens /api/v1/ only;
    `GateRules.First` = the app's own rules first - vDeploy's helper port and webhooks), `Tabs` (KeyOf / RankOf virtual - vDeploy's
    `DeployTabs`), `StatusLine`, `Clock`, `Dialogs` / `ConfirmRequest`, `IWebState` (the app's state - register it), `WebApp` (Message,
    CardLogo, AfterSetup, Assembly).
  - The window: `SuiteWindow` (slots Logo / Menu / Buttons / Side / ChildContent; `Live(path)` + `Forget()` for remembered tabs; `Dirty`;
    Options and Sign out are its own), `SuiteCard` + `SuiteBareLayout` (the sign-in card), `OptionsFrame` (page list, brand, title),
    `SignInOptions` (Options > Sign-in: login, authenticator, trusted addresses, staying signed in, app keys, dangerous actions).
  - Pages `/login` and `/setup`: the app's router needs `AdditionalAssemblies = WebApp.Assembly` and MapRazorComponents
    `.AddAdditionalAssemblies(WebApp.Assembly)`.
  - Parts (namespace vSuite.Web.Parts): Icon (add glyphs here), Toggle, Seg, Row, UseBar, Crumbs, ConfirmDialog.
  - `wwwroot/vsuite.css` (window, menus, pages, cards, buttons, tables, code, forms, Options, confirmation, sign-in, phone width) and
    `wwwroot/vsuite.js` (`window.vs`: layout memory under `<html data-app>`, splitter, Ctrl+B, tab wheel, copy, focus, keepEnd) - link
    both before the app's own app.css / app.js.
  - A new server app = these + its own pages, side panel, menu and app.css.

## Which app is running: `App.Init`

Each app calls `App.Init(name, envPrefix, updateSite, installStyle, folderMarker)` first thing in Main (updateSite =
"https://vsuite.vrsion.com"). Nothing in the library names an app. From it: `App.DataFolder` / `Log.Folder` (%APPDATA%\Name, or
PREFIX_DATA_DIR), the log name (name.log), `Instance.Prop` ("Name.Instance"), the mutex, the test variables (`App.Env("TEST_HOOKS")`,
`TEST_ASK`, `VSHELL_DIR`, `UPDATE_SITE`), `Updater`'s exe / folder names. `AppIcon.Drawer` = the app's own drawing (set at start-up too).

## The update site (since 1.0.18 - GitHub before)

Only each app's newest release is on https://vsuite.vrsion.com (nginx, SFTP-only account `my_webapp__2`, site root `/www`): `name/latest.json` + `Name.exe` (vView:
`vView.zip`) + `Name-setup.exe`; the newest pre-release in `name/pre/`. vShell's plugins: `vshell/plugins/<plugin API>/` (plugins.json +
the DLLs; `vshell/pre/plugins/<API>/` for a pre-release). latest.json = app, version, published, prerelease, notes, install, installer,
files {name: {size, sha256}} and anything else (vShell: plugin_api -> `UpdateInfo.Fields`). `changelog.md` beside it (the app's CHANGELOG.md):
a found update's `Notes` = every section since the running version (`ChangesBetween`). `scripts\site-release.ps1` (1.0.28: also marks the version's heading in the app's CHANGELOG.md "· pre-release" / "· release" - commit it after the build) writes a
folder; each app's build.cmd calls it into `dist\site\` (`build.cmd pre` for a pre-release). `scripts\suite-core.ps1` (1.0.26) - each app's build.cmd runs it first: the submodule up to the newest library's main, committed in the app as "<version>: vsuite.core X" if it moved. `scripts\test-send.ps1` - sends a test command (WM_COPYDATA kind 0x76580002) to an app started with PREFIX_TEST_HOOKS=1 (vXplorer, vPad, vTerm): nothing moves the real mouse. `scripts\site-upload.ps1` puts
`dist\site\` on the site (`build.cmd upload`, `build.cmd pre upload`): Windows' sftp.exe with the key in V's `~/.ssh/config` entry
`vsuite-site` (`~/.ssh/vsuite_site`, no passphrase); each file as `.part` then renamed over the old one (the server's posix-rename),
every latest.json last, nothing deleted; then each latest.json and its files are checked over https. Only when V asks for a build.

## What's here (and where it came from)

- `Core/Log` - vShell's: queued, written every 0.5 s on a timer, repeats counted, 1 MB then name.old.log. **Call `Log.Flush()`** before
  the process can die (unhandled-exception handlers, FailFast hooks); a normal exit flushes itself.
- `Core/VShellTheme` - `VShellColours` + `VShellThemeSource` (vXplorer's) + the Wallpaper Theme (colours from `WallpaperColor`, moved
  from vShell) + a 15 s poll: vShell's folder appearing later, a new wallpaper.
- `Core/WallpaperColor` - vShell's k-means palette, unchanged; `FindWallpaper` asks IDesktopWallpaper for the monitor at 0,0 (vShell sets
  its pictures through Windows, so it's the same file), then the registry, then TranscodedWallpaper. vShell passes its own path.
- `Core/Updater` - one-exe (vXplorer, vShell: swap the exe, `--update-watchdog` in the old exe) and folder (vView: zip unpacked as
  Name.update, PowerShell watchdog swaps folders, copies `unins*`) installs; both refuse a build folder (`CantUpdateReason`), honour
  PREFIX_UPDATE_SITE, log install failures. `BeforeSwap` hook (vShell: plugins), `ReleasesAsync(app, pre)`, `UpdateInfo.Folder` /
  `Fields`, `IsUpdateRestart`.
- `Core/Secret` (1.0.27, DPAPI "dpapi:" - vShell's plugin API Secret passes to it), `Core/VTermConnections` (1.0.27: `VTermConnection` + reading vTerm's settings.json Connections, read only, cached by write time; `Read<T>` for vShell's own type), `Ui/FormWindow` (1.0.27: FormWindow / FormField / Looks - vTerm, vPad; vShell has its own, plugin API - it aliases its names in GlobalUsings), `Ui/CornerWindow` (1.0.27: CornerWindow + NoticeWindow - vView, vTerm).
- `Core/Instance` (`InTray(hwnd, on)` 1.0.29: a window hidden in the tray still gets a second start's hand-off), `Core/Format` (`TimeText.Stamp`, 12-hour).
- `Ui/TrayIcon` (1.0.30): the notification-area icon for every app (vTrace, vShell) - Shell_NotifyIcon on its own hidden pop-up window (not
  message-only: it must hear TaskbarCreated), the old notification version (one WM_LBUTTONUP / WM_RBUTTONUP per click - version 4 sent
  WM_RBUTTONUP and WM_CONTEXTMENU both, and the menu didn't work). `Menu` builds the ContextMenu, `MenuTheme` dresses it (Controls.xaml +
  `Theme.Apply` on the menu's own resources - vShell's menus have no app-wide styles), a 50 ms watch closes it on a press outside its windows /
  Esc / another program in front (a menu from the tray never hears it lost the focus). `SetIcon(BitmapSource)` (no System.Drawing), `Size`.
- `Core/TermLink` (1.0.20): vShell -> vTerm. `TermRequest` (Cmd toggle / show / hide / open-host / args, the drop-down's place and the
  bar's look, the host to open), `TermLink.Send(pipe, request)` (null = vTerm isn't running; lets vTerm take the foreground) and
  `Serve(pipe, handle, stop)` (vTerm). Pipe = "vTerm-" + `Instance.IdFor(vTerm's data folder)`, CurrentUserOnly.
- `Ui/Glass` (`Backdrop` + `Glass`, from vShell's Backdrop): Blur / Frosted = the blurred wallpaper (per monitor, 1/8 size, IDesktopWallpaper's picture and fit), both off = the wallpaper as it is ("Clear", half size), under the content - always, as the bars - the content's vx.Bg (theme colour at the theme's opacity) over it - **never Windows' Mica / Acrylic** (Mica hid the opacity, Acrylic shows the windows behind). Windows call `WindowEffects.Look(window, theme, blur)` on every theme change (after their content is set).
- `Ui/Theme` (vShell colours -> `vx.*` resources), `Ui/WindowEffects` (DWM: dark mode, corners, outline, caption colour; `Look`), `Ui/Controls.xaml` (every style; apps merge
- `Ui/SnapLayouts` (1.0.44): `Attach(window, maxButton)` - Windows 11's layouts menu on a WindowChrome window's own maximise button (WM_NCHITTEST = HTMAXBUTTON over it; Windows then has the mouse, so the hover / pressed looks (vx.Hover / vx.Active as a local Background) and the Click (on WM_NCLBUTTONUP after a down there; the down is never passed on - Windows would draw its old button) are done here). Only while `IsOn`: build 22000+, `WindowArrangementActive` (Control Panel\Desktop) and `EnableSnapAssistFlyout` (Explorer\Advanced) not "0" - read at most every 3 s; off = the plain button. Hooked on Loaded, after WindowChrome's hook (HwndSource asks the newest first; WindowChrome answers every hit test). Apps using it: vShell's Settings (0.38.245).
  `pack://application:,,,/vSuite.Core;component/Ui/Controls.xaml`), `Ui/Ask` (themed question; owner = any `IThemedWindow`),
  `Ui/AppIcon` + `Ui/WindowIcons` (WM_SETICON 64/16 px, `vShell.LiveIcon`, `Instance.Prop`), `Ui/UpdateCenter` (+ `IUpdateSettings`),
  `Ui/UpdateNotice` (a Border the app places), `Ui/TabStrip` (1.0.21: vXplorer's tab strip as a control - `StripTab` per tab (title, icon, chip, unsaved dot, line colour), the width rule `WidthFor`, scroll arrows, drag along / tear out / onto another strip (`Live` list; `OwnsWindow` for vTerm's text window), events Picked / CloseClicked / MenuRequested / Reordered / MovedTo / TornOut / PressEnded; vPad, vTerm and (1.0.22) vXplorer use it; `DropHighlight` / `Element` for vXplorer's files dropped on a tab), `Ui/OptionsWindowBase` (vShell's settings look: pages, SubPage + LinkRow, cards, Section, Tabs, rows with the (i) help card, Switch, Segmented, Combo, SliderBox, TextField, Chip, Note, the Updates page; Forms.xaml on the window).

## Rules

- House rules as in the apps: plain friendly UI text, 12-hour times, every colour a `vx.*` DynamicResource.
- Changing something here changes every app: keep behaviour the same unless the change is meant for all of them, add a test where it
  has no window, add a line to CHANGELOG.md (`## 1.0.N`) and bump `<Version>` in both csproj files.
- Commit messages: `1.0.N: what changed` (lower case after the colon).
- vShell's plugins call vShell's own `Log` / `Theme` by name: when vShell moves over, those names stay in vShell as thin wrappers.
