# Changelog

## 1.0.50

- scripts\sign.ps1 + sign.cmd: the Windows apps' builds sign the app, its installer and uninstaller with the Certum certificate (SimplySign Desktop connected); an upload needs it.

## 1.0.49

- vSuite.Web `PlaceLink`: `AndBelow` (also picked on the pages under it) and `CountTitle` (the count's own tooltip) - for vDeploy's side panel.

## 1.0.48

- Notifications to Discord too (a channel's webhook): each group goes to any of Gotify, Discord and vShell, ticked in Options › Notifications. Choices made before keep their meaning, with Discord beside Gotify. Apps' settings gain `DiscordWebhook`.

## 1.0.47

- vSuite.Server: the server apps' Activity log (`ActivityEvent`, `ActivityLog` / `ActivityLog<T>`) and notification routing (`Notifier`, `NotifyGroup`, `INotifySettings`) - shared instead of copied; reads vDeploy's older activity.json.
- vSuite.Web: `NotificationsOptions` (Options › Notifications: Gotify and what goes where), `Dot`, and the side panel's `PlaceLink` / `SideHead`.
- vSuite.Server.Tests: tests for them.

## 1.0.46

- SnapLayouts: a maximise button that isn't a WPF Button, or is made again (vTerm's strip) - its own click and hover look.

## 1.0.45

- SnapLayouts: Windows' setting is read (1.0.44 never read it, so the menu never showed).

## 1.0.44

- SnapLayouts: Windows 11's snap layouts menu on a window's own maximise button (only while Windows' Show snap layouts setting is on - otherwise the button is as it was).

## 1.0.43

- Glass: one picture of the whole desktop (every monitor's wallpaper in its place), made once - a window dragged across monitors or past a screen's edge is as smooth as on one monitor (1.0.42 drew the monitors' pictures again at every step).

## 1.0.42

- Glass: a window across two monitors shows each one's wallpaper where that monitor is, and one dragged past a screen's edge keeps its glass moving with it (it stopped at the edge).
- Glass: while its window is dragged, the content over the glass is held as a picture and only the glass is drawn again - big windows drag smoothly.

## 1.0.41

- Glass: the wallpaper's file is looked at off the UI thread - a wallpaper on a network share that has gone away no longer holds every window for seconds, every 4 seconds.
- TermLink: Send knows at once when no vTerm is listening (`Listening`) instead of trying for a second and a half at full speed - vTerm starts faster.

## 1.0.40

- vSuite.Web: a path that ends like a file (/options/a.css) or /not-found no longer shows a page without signing in.
- vSuite.Web: a listed proxy that doesn't say who it passes on is never taken for the visitor (its own address isn't tested against the trusted ones).
- vSuite.Web: a trusted address only skips the sign-in on the app's own names - an address, a home-network name, or a name somebody has signed in on with the password (names.json). Stops a page that points its own name at the app (DNS rebinding).
- vSuite.Server: JsonFile - a file that can't be read is never taken for an empty one: a copy is kept (.broken-<time>), the save before (.bak) is used, and with must the app stops instead. Saves are flushed to the disk.
- Updater: the update's watchdog no longer ends a copy that still has a window open (asking to save) after 20 s - it waits for it to close.

## 1.0.39

- vSuite.Web: a trusted address no longer lets another site's page use the app - a request a browser sends from a different site (its Origin) has to sign in.

## 1.0.38

- vSuite.Web: SuiteWindow's + beside the tabs is left out when PlusPath is empty.

## 1.0.37

- vSuite.Server: SshRunner.OpenShell - a terminal (pty) on the host, for a terminal in the page.

## 1.0.36

- vSuite.Web: the app's name next to its icon at the top left (hidden on a phone).

## 1.0.34

- vSuite.Web: a "vm" icon (a screen) for virtual machines.

## 1.0.33

- vSuite.Web: the window (SuiteWindow, SuiteCard), the stylesheet (vsuite.css), Sign in and Setup pages, the Options frame and Options > Sign-in.

## 1.0.32

- vSuite.Web: the server apps' shared page parts - vShell's colours (WebTheme, ThemeStore), the sign-in gate, tabs, status line, clock, confirmations, Icon / Toggle / Seg / Row / UseBar / Crumbs / ConfirmDialog, vsuite.js.

## 1.0.31

- vSuite.Server: what the server apps (vDeploy, vPatch) share without a page - data folder, log, sign-in pieces, SSH with the app's own key, updating itself.

## 1.0.30

- Ui/TrayIcon: the apps' notification-area icon - click, a themed right-click menu that closes when you click elsewhere, a picture or the exe's icon. The design guide says when and how to use it.

## 1.0.29

- Instance.InTray(hwnd, on): a window hidden in the notification area still gets a second start's hand-off (HandOff / SendTo found only visible windows).

## 1.0.28

- scripts\site-release.ps1 marks the built version's heading in the app's CHANGELOG.md: " · pre-release" (build.cmd pre) or " · release" (a release replaces the pre-release mark). Commit the app's CHANGELOG.md after a build.

## 1.0.27

- Shared now instead of copied into the apps: Secret (DPAPI, the "dpapi:" form - vShell, vTerm, vPad), FormWindow / FormField / Looks (vTerm, vPad), CornerWindow / NoticeWindow (vView, vTerm), VTermConnection / VTermConnections (vTerm's saved connections, read only - vShell, vPad). scripts\test-send.ps1 (test hooks).

## 1.0.26

- scripts\suite-core.ps1 (from vShell's): every app's build.cmd runs it first and builds with the newest library (committing the submodule if it moved) - only vShell's did, and the others drifted onto older libraries.

## 1.0.25

- Settings / Options windows get the new scrollbars too: Forms.xaml's own (older) ScrollBar style, merged after Controls.xaml, overrode it there - gone. Options' page keeps clear of the window's right edge (it's resized from there).

## 1.0.24

- Updates: the download (and its checksum) runs off the window's thread and reports progress each 0.1 % - the update notice's bar follows the download instead of standing still until the end (each chunk and progress call was queued ahead of drawing). An app installed from vShell's Settings downloads the same way.

## 1.0.23

- Scrollbars easier to grab: 12 px to aim at (was 10), the thumb widening from 6 to 10 px when the pointer is on the bar or it's dragged, and never shorter than 40 px however long the content (ThumbMinConverter on the Track's ViewportSize).

## 1.0.22

- TabStrip: StripTab.DropHighlight (files dragged over a tab) and Element (an app's own events on a tab), TabStrip.Clear. vXplorer uses the strip now too.

## 1.0.21

- TabStrip / StripTab: vXplorer's tab strip for every app - tabs sharing the room with arrows when they don't fit, a tab dragged along the strip sliding the others aside, pulled out as a floating copy (another window's strip: MovedTo; the open: TornOut), a window's only tab moving the window. vPad and vTerm use it.

## 1.0.20

- TermLink / TermRequest: the line between vShell and vTerm - a named pipe for your own processes only (one JSON request per connection, a line back), for the drop-down terminal from vShell's bars, Open shell, and a second vTerm start handing over to the running one. The sender lets vTerm come to the front.

## 1.0.19

- scripts\site-upload.ps1: puts an app's dist\site\ on the update site by SFTP (each app's `build.cmd upload`) - files as .part then renamed over the old ones, latest.json last, then checked over https.

## 1.0.18

- Updates come from the suite's web site (App.Init's third value is now the site, "https://vsuite.vrsion.com") instead of GitHub - no more "GitHub is limiting requests". Each app's newest release is in site/name/ (latest.json + its files), the newest pre-release in site/name/pre/. latest.json carries each file's size and SHA-256, so there are no .sha256 files. PREFIX_UPDATE_SITE replaces PREFIX_UPDATE_API for tests.
- Updater: ReleasesAsync(app, pre), ParseRelease, PickNewest(releases, current), SiteFolder, NewestReleaseAsync(app, pre); UpdateInfo has FileName / Sha256 / Installer / Files / Folder / Fields (Assets, HashUrl and AssetsForAsync are gone).
- The update notice lists every version's changes since yours: the release's changelog.md (the app's CHANGELOG.md, beside latest.json) is read when a new version is found (Updater.ChangesBetween); without one, latest.json's notes.
- SuiteApps.FindInstallerAsync(app, pre) reads the site; the installer download is checked against its SHA-256.
- scripts\site-release.ps1: writes an app's site folder (files + latest.json) - each app's build.cmd calls it.
- Options > Updates: "Releases page" is now "vSuite website".

## 1.0.17

- SuiteApps: an app's installer is found as Name-setup.exe (the same name in every release, so a web page can always link to the latest) as well as the older Name-setup-version.exe.

## 1.0.16

- ShellIcons: finished icons and thumbnails reach the UI thread in batches (at most 15 ms each) instead of one dispatcher trip apiece - a 544-item folder's icons all show in ~0.3 s instead of ~0.8 s.

## 1.0.15

- ShellIcons: thumbnails ask Windows' thumbnail cache first (two threads, ~1-2 ms each) and only make the ones it doesn't have (four threads) - thumbnails seen before no longer wait behind new ones.
- ShellIcons: files' own icons (.exe, .lnk, .ico …) load on three threads instead of one (~2.5x faster in a folder full of them).

## 1.0.14

- ComObject.Create: the desktop wallpaper object made by its CLSID (Glass, WallpaperColor) - an app that made it first through its own class no longer breaks the library's lookups, nor the library the app's.

## 1.0.13 (review fixes)

- UpdateCenter: "--update" counts for one check only (a failed check no longer leaves it waiting to install by itself later), and never installs a skipped / rolled-back version.
- Updater: a release without its .sha256 isn't installed; `Updater.CanSwap` lets the app hold an update back (vXplorer while a copy runs) before anything is swapped.
- Ask: the default button has the focus to start with, and Enter presses the focused button.
- Glass: one IDesktopWallpaper for the app, the desktop colour cached with the wallpaper key, and a plain-colour desktop no longer shows Windows' leftover TranscodedWallpaper.
- NewestReleaseAsync(repo, pre, appName): only releases that app can install; SuiteApps.UpdateSkip(name).

## 1.0.12

- Another app's newest release / installer: GitHub's limit (403 / 429) says "GitHub is limiting requests right now - try again later", as the update check does.

## 1.0.11

- SuiteApps.FindInstallerAsync / DownloadInstallerAsync: an app's newest installer (the newest release with Name-setup-*.exe attached) - vShell's vSuite apps card installs an app that isn't there.

## 1.0.10

- SuiteApps: another app's Include pre-releases - read from its settings file, changed through its running copy (Instance.SendTo) or in the file. UpdateCenter takes "--update-pre on|off"; Options' switch follows a change made from vShell.

## 1.0.9

- Updater.NewestReleaseAsync(repo): another app's newest release (vShell's Settings lists the suite).
- UpdateCenter.HandleCommand("--update"): check now and install when there's a new version - an app started with it, or handed it while running.

## 1.0.8

- Glass: a desktop with a plain colour (no picture) shows that colour, not black; Glass can be derived from (vShell's bars and widgets use it now, with their own blur setting).

## 1.0.7

- Options match vShell's settings: the (i) opens vShell's help card (not a tooltip), the form controls (Forms.xaml) are on the window, rows have vShell's spacing, and vShell's blocks are there to use - LinkRow + SubPage (rows that open a page, with a back arrow), Section (foldable), Tabs, Combo, SliderBox, TextField, Chip.

## 1.0.6

- See-through can be switched off: Theme.Apply(r, seeThrough) and WindowEffects.Look(w, t, seeThrough) - false = solid, no glass layer (the apps' "Use the theme's opacity" option). Ask follows its owner.

## 1.0.5

- Blur and Frosted off: the windows show the wallpaper as it is (not blurred) under the theme colour at the theme's opacity, as vShell's bars over the desktop - they were solid before. The background always keeps the theme's opacity (Theme.Apply).

## 1.0.4

- Glass + Backdrop (from vShell): Blur and Frosted as vShell's bars draw them - the blurred wallpaper under the theme colour at the theme's opacity - instead of Windows' Mica, which hid the opacity. WindowEffects.Look puts it under a window's content; Ask and Options use it.

## 1.0.3

- Forms.xaml: the form controls from vShell's settings (buttons, switches, sliders, drop-downs, text and password boxes, lists), on the vx.* colours.
- Look.Dress: the suite's styles and brushes on one window (for vShell, whose bars and widgets keep their own look).
- Theme: readable text with an odd theme (vShell's fix), control colours (vx.Ctrl / CtrlHover / CtrlBorder / SelBg), cards and the page list a little stronger (as vShell's settings).

## 1.0.2

- Ask: one to three buttons with a default and a cancel one, an optional glyph, a placement hook, and a fallback theme - so vShell can show its questions in the theme. In an app without the suite's styles (vShell) the question carries them itself.

## 1.0.1

- Updater: ReleasesAsync reaches back 40 releases, and AssetsForAsync gives one release's files (for vShell's plugins).

## 1.0.0

- First version, built from vXplorer's and vView's copies and vShell's originals: the vShell theme (now with the Wallpaper Theme and
  noticing vShell installed later), glass and styles, themed questions, the log (written in the background, repeats counted), one
  running copy, the app icon and window icons, updates for one-exe and folder apps (with the build-folder check and the test address
  for every app), the update notice, and the Options building blocks with the Updates page.
- vSuite.Shell: Windows' icons and thumbnails (ShellIcons).