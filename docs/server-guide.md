# vSuite server guide

How a vSuite **server app** looks, behaves and is put together - the Linux web apps that live in a Proxmox LXC and are used from a
browser (vDeploy, vPatch, vTrace, vMediarr, vProxy), taken from the apps as they are. The companion of `design-guide.md` (the Windows
apps): where this guide says nothing, that one's rules hold (words, 12-hour times, theme colours only). Follow it for a new server app and
for new parts of an existing one. Where the apps differ, **vProxy's way** (the newest) is the one to copy unless said otherwise. When an
app has to differ, write down why in its CLAUDE.md; when the suite changes its mind, change this guide in the same commit.

Contents: [Principles](#1-principles) · [The window](#2-the-window-in-a-browser) · [Pages and words](#3-pages-and-words) ·
[Options](#4-options) · [Sign-in and keys](#5-sign-in-and-keys) · [The API](#6-the-api-for-vshell-and-vtrace) ·
[Activity and notifications](#7-activity-and-notifications) · [Data and secrets](#8-data-and-secrets) ·
[Install, update and root](#9-install-update-and-root) · [Demo and tests](#10-demo-mode-and-tests) ·
[Build, release, website](#11-build-release-website) · [New server app checklist](#12-new-server-app-checklist)

---

## 1. Principles

1. **A homelab app.** One app, one job, one unprivileged Debian 13 LXC on a Proxmox node, made by one line in the node's shell. No
   Docker image offered (vDeploy keeps its Dockerfile in the repo, unused), no database server - files in the data folder (vMediarr:
   SQLite, as the apps it replaces).
2. **From any browser** - a PC, a phone, a Linux laptop. It looks like a vSuite window (2) and wears vShell's theme once vShell's plugin
   sends it; without vShell it's vShell's default theme and everything still works.
3. **Nothing exposed by default.** Plain http on the LAN (`http://<LXC>:84x0`), signed in, a home network / VPN as the only way in. The
   app's own pages are never made public (vProxy's are never routed outside; they always answer directly on the LXC's address, so a
   proxy change can't lock you out).
4. **Read-only on what isn't ours.** Other machines are read unless you press a button: no change to a host, a node or another app without
   a page saying what will happen. Our own user and key on a host, never yours; a password given once is never kept. An import never
   writes to the app it imports from (vProxy's NPM import, vMediarr's Radarr / Sonarr / Seerr).
5. **The app never has root.** It runs as its own user; anything that needs root goes through a small root unit made by install.sh (9).
6. **Plain words.** As the Windows guide: "Check now", "Put v12 back", "Couldn't reach nuc01: <reason>".
7. **Keeps itself up to date** from vsuite.vrsion.com, with the old version put back if the new one doesn't start.
8. **Talks to the suite**: vShell's plugin shows it (bar, popup, widget, notifications) and sends the theme; vTrace reads its events as
   changes; the apps link to each other with app keys (vProxy reads vDeploy's services and vTrace's changes).

| App | Port | Key prefix | Site folder | Extra ports / programs |
|---|---|---|---|---|
| vProxy | 8450 | `vx_` | `vproxy/` | 8451 sign-in gate, 8452 tunnel, 8453 cloudflared (all 127.0.0.1); Caddy on 80 / 443 |
| vDeploy | 8460 | `vd_` | `vdeploy/` | 8461 helpers (TLS, its own certificate) |
| vPatch | 8470 | `vp_` | `vpatch/` | |
| vTrace | 8480 | `vt_` | `vtrace/` (copied to `vtrace-server/` too, for now) | |
| vMediarr | 8490 | `vm_` | `vmediarr/` | 8989 / 7878 / 5055 (Sonarr / Radarr / Seerr compatibility, when on) |

A new app takes the next free 84x0.

## 2. The window in a browser

ASP.NET Core + Blazor Server (.NET 10), every page interactive (Server) except `/login` and `/setup` (vSuite.Web's, static - they set the
cookie) and vMediarr's `/ask*` (static on purpose: a live circuit could reach the admin's pages).

- **`MainLayout` = `SuiteWindow`** (vSuite.Web) with the app's slots: `Logo` (its `AppLogo` at 18 px), `Menu`, `Buttons` (vDeploy's
  refresh), `Side` (its `SidePanel`). `BareLayout` = `SuiteCard` / `SuiteBareLayout` (sign-in, errors). Every app sets `PlusPath=""` -
  the side panel opens everything, there's no + beside the tabs.
- **Caption row:** the app icon + name (its menu), side panel (Ctrl+B), tabs, the app's buttons, Options. **The menu:** the app's places
  (Hosts, Add a host, History, Activity …) - separator - Options, About <App>; SuiteWindow adds Sign out.
- **Tabs** (`Tabs`, an app subclass: `ProxyTabs`, `DeployTabs`, `PatchTabs`, `TraceTabs`): a tab is a key (`KeyOf`) plus the page it
  shows now - one tab per *thing*, not per page ("avoid a thousand tabs" - V): `options`; the main list and its add / import pages;
  one tab per host with everything under it. Order fixed by `RankOf` (Options 0, the main list 1, … Activity / History / Updates last);
  dragging stays in the group. Pages call `Tabs.Show(Nav.Uri, title, icon)` with a "where am I" title ("nuc01 › arrstack"), `Crumbs`
  draws Back + the trail. Remembered per browser (`vs.save`); `Live(path)` drops tabs whose thing is gone; the last tab closed = `/empty`.
- **Side panel:** the app's places at the top, then its things with a `Dot` and counts; resizable (160-520 px, remembered).
- **Status line** (`StatusLine.Left` / `Right`): facts on the left, a short note on the right ("Checked 3:42 PM").
- **Phone width:** `vsuite.css` does it (under 760 px one column, the side panel covers the page and closes when you go somewhere; under
  600 px the app name hides). A grid column for a hidden panel collapsed the page to nothing once - use the library's layout.
- **Theme:** `ThemeStore.Css(settings)` writes `WebTheme`'s CSS variables into the head; **only variables, never a colour literal**:
  `--bg`, `--bg-fill`, `--fg`, `--fg-dim`, `--accent`, `--on-accent`, `--secondary`, `--panel`, `--panel-border`, `--popup`, `--side`,
  `--hover`, `--active`, `--selected`, `--line`, `--ctrl*`, `--sel-bg`, `--track`, `--thumb`, `--code`, status `--ok` / `--warn` /
  `--bad` / `--str`, corners `--radius` / `--item-radius` / `--menu-radius` / `--tab-radius`, text `--fs`. Pages redraw on
  `ThemeStore.Changed`.
- **Type and glyphs:** Segoe UI 12.5 px (Larger = 14 px), Consolas for addresses, keys and logs. Glyphs are SVGs from vSuite.Web's `Icon`
  (phones and Linux have no Segoe Fluent Icons) - a new glyph is added there, not in the app. No emoji.
- **Styles:** link `_content/vSuite.Web/vsuite.css` and `vsuite.js` before the app's own `app.css` / `app.js`; app.css holds only the
  app's own pages. `window.vs` gives layout memory, the splitter, Ctrl+B, the tab wheel, `vs.copy` (falls back to execCommand on plain
  http), `vs.focus`, `vs.keepEnd` (follow a log only while at the bottom), `vs.dirty` (asks before closing a tab with unsaved edits).
- **Times:** the browser's zone (`Clock`, set by `vs.start`); pages that show times redraw on `Clock.Changed` (the first draw comes
  before the zone). The LXC runs in UTC - a schedule saves the browser's IANA zone with its time (vDeploy, vPatch).
- **The icon:** `AppLogo.razor` (an SVG drawn by the Windows guide's rules - outline shape in the Main Accent, the three dots) +
  `favicon.svg`; `WebApp.CardLogo` puts it on the sign-in card.

## 3. Pages and words

- **Activity** (`/activity`, `ActivityPage.razor` - a component named `Activity` hides System.Diagnostics.Activity): what the app did and
  saw, newest first, each with its look (ok / info / warn / bad), a link to its page and, where there is one, its output.
- **History / versions** where the app changes things: vProxy keeps every change as a numbered version (`versions/NNNN.json`, newest 200,
  `Diff`, "put back" = a new version); vDeploy keeps each stack's versions (file SHA-256s as the host computes them + image digests,
  rollback pins them). vPatch, vTrace and vMediarr have none.
- **Questions** are the page's own: `Dialogs.Ask` / `ConfirmRequest` -> `ConfirmDialog`, never `alert` / `confirm`.
  - **Dangerous actions** (removing a host, putting an old version back, a shell as root, making a site public): `Options > Sign-in >
    Before a dangerous action` = type the name (with Copy), your password, or both (`IWebSettings.Confirm`).
  - **Anything that changes signing in** always asks for the password (SignInOptions does it); so does making an app key.
- **Errors** say what happened and what to do; a page that throws is a 500 drawn by `Error.razor`, which logs the exception.
- **Busy:** buttons say what they're doing ("Checking…", "Asking…") and are disabled meanwhile; long work runs in the background and the
  page follows it - a job outlives the page (and, on a host, the connection: `setsid nohup` + a log the app follows - vDeploy, vPatch).
- **Banners** (`.banner ok|warn|bad`, with a Hide) for a state you should know about: an update that failed, a lost vDeploy service.

## 4. Options

`/options/{page}` with vSuite.Web's `OptionsFrame` (page list with the brand, cards of `Row`s with hints; every change saved at once, no
OK). Rows: `Toggle` (on / off), `Seg` (2-4 choices), `<select>`, inputs (`input mono` for addresses and keys). A secret's box is
`type="password"`, empty, with the placeholder "kept - paste to change it" once there is one - a stored secret is never shown again.

**Pages, in this order:** General · the app's own pages · Connections · Notifications · Sign-in · Look · Backups · Updates · About.

| App | Its own pages (between General and Sign-in) |
|---|---|
| vProxy | Certificate, Outside access, Connections, Notifications, Error pages, Import |
| vDeploy | Notifications |
| vPatch | Notifications |
| vTrace | Sources, Local AI, Notifications |
| vMediarr | Library, Files, Profiles, Indexers, Download clients, Requests, Compatibility, then Connections and Notifications as the others (0.0.31) |

- **General:** the app's defaults and timings (check every N hours …), the proxies allowed to pass on the visitor's address (vDeploy), and
  for apps with SSH hosts **Hosts** (vSuite.Web's `HostsCard`: export the ticked hosts as a "vsuite-hosts" file - name, address, port, SSH
  host key, OS, CPU, never a user / password / key - and import one; vDeploy, vPatch, vTrace) and the app's SSH key.
- **Connections:** other apps it reads, each an address + an app key from that app's Options › Sign-in › Apps ("kept encrypted"), a
  **Try it** button, and a note on what it reads and the version it needs.
- **Notifications:** Gotify (address, app token, Send a test), then **What goes where** - one row per group with `Seg` Off / Gotify /
  vShell / Both (7).
- **Sign-in:** `<SignInOptions ClientIp=… KeysHint=… ActionsHint=… />` - nothing of the app's own.
- **Look:** "Use vShell's colours" (who sent them and when, else "None have come yet, so vShell's default theme is used"), the swatches
  (background, text, accents, ok / warn / bad), Text size Normal / Larger.
- **Backups** (1.0.51): `<BackupsOptions Backups=… Settings=… Saved=… Restart=… What="…" />` - Back up now, Also copy to (a folder a
  reinstall won't wipe), Restore from a file, the list with Download / Restore (8).
- **Updates:** a banner for "updated itself to x" / "the update to x didn't start, so y was put back"; This <App>, The newest + Check now,
  Include pre-releases, then "<App> x is out" with Update now and What's new (the changelog sections since yours); "Updates come from
  vsuite.vrsion.com … checked a minute after <App> starts and every six hours." Pinned parts the app ships go here too (vProxy: Caddy and
  cloudflared, 9).
- **About:** the logo at 48, "<App> x.y.z" and one sentence ending "Part of vSuite."; **Where things are**: Data folder ("… and the log
  (<app>.log). Back it up as a whole."), anything else it relies on (vProxy: Caddy's socket), Running on (OS · .NET); **Made with**: each
  part with its makers and licence ("Microsoft · MIT licence" for ASP.NET Core and Blazor).

## 5. Sign-in and keys

All of it is vSuite.Server + vSuite.Web; an app only provides settings that implement `IWebSettings` (`ISignInSettings` + `Confirm`,
`VShellColours`, `TextSize`) and registers its `AppState` as `IWebState`.

- **Setup:** no login yet = every page goes to `/setup` (user name + a password of 10+ characters, `Password` = PBKDF2-SHA256 210,000);
  after it `WebApp.AfterSetup` (vDeploy `/hosts/add`, vProxy `/options/certificate`).
- **Cookie:** named after the app (`vproxy`), HttpOnly, SameSite Lax, sliding, `StayDays` long; "Sign out every device" raises `Stamp`
  and `OnValidatePrincipal` rejects older cookies. Data protection keys in the data folder (`keys/`).
- **Authenticator:** optional TOTP (`Totp`, 6 digits, a step either side), its secret kept with `Secrets`.
- **Trusted addresses** ("Go straight in from these addresses"): ranges with names (home LAN, the VPN). The `Gate` trusts one only when
  the request didn't come through a proxy it wasn't told about, its Origin isn't another site's, and the name it was sent to is the app's
  own (an address, a plain / .lan / .local / .home.arpa name, or one somebody signed in on - `KnownNames`, names.json) - DNS rebinding.
- **Proxies:** `TrustedProxies` may pass the visitor's address (the last X-Forwarded-For entry); nobody else's header counts. The other
  apps list vProxy as their one trusted proxy.
- **Lockout:** 5 wrong sign-ins from one address in 15 minutes lock it out for 15 minutes.
- **App keys** (Options › Sign-in › Keys for apps, made with the password): `ServerApp.KeyPrefix` + 48 hex, shown once, only the SHA-256
  kept, a name ("vShell", "vProxy") and Made / Used (updated at most every 10 minutes). `Authorization: Bearer <key>` opens **`/api/v1/`
  only**, never a page; the key's name is the caller (`Auth.KeyItem` - "from vShell" in Activity). Remove the key to stop that app.
- **The gate's exceptions** go in `GateRules.First` (Pass / Stop / Next), never in the gate: vDeploy's helper port and `/hook/<secret>`,
  vMediarr's `/ask*` and `/feed/`. Each such endpoint checks its own secret with a fixed-time compare. Open without anything:
  `/api/health`, `/_framework`, `/_content`, static files, `/Error`.

## 6. The API for vShell and vTrace

`Api.cs`: `app.MapGroup("/api/v1")`, JSON with web defaults (camelCase, nulls left out); a refusal is `{ ok: false, message }` with its
status. **Field names are a contract** - vShell's plugin and vTrace read them: add fields, never rename one (or raise a version).

- `GET /api/health` (open): `{ ok: true, version }`.
- `GET /api/v1/overview`: `version`, `lastEvent` (the newest event's id - the plugin's cue to fetch events), the app's things with their
  state in a few words (`answering`, `why`, `since`, counts), totals, and any job running. Cheap: from memory, no live check.
- `GET /api/v1/events?after=N&limit=` (oldest first, after N) or without `after` (the newest, default 50): `{ events, last }`.
  `GET /api/v1/events/{id}` = one, with its output (`Body`) where the app keeps one.
- `POST /api/v1/check` (and per thing, `/hosts/{id}/check`): look now. Actions the plugin offers (redeploy, upgrade, guest power) are
  POSTs that start the job before answering and go through the same code as the page - with its dry run and checks.
- `PUT /api/v1/theme`: a `WebTheme` - `Background`, `Background2`, `Foreground`, `MainAccent`, `SecondaryAccent`, `Border`, `Gradient`,
  `GradientAngle`, `Radius`, `BorderOpacity` (case-insensitive, #AARRGGBB is fine) -> `themes.Set(t, caller)`. Keep these names.
- App-specific: vTrace `/changes`, `/changes/{id}`, `/pc` (vShell posts each PC's Windows-log changes), `/look`; vDeploy stacks and
  nodes; vPatch `/hosts/{id}/upgrade`, `/upgrade`; vMediarr's calendar feed.
- **vShell's side** (each app's plugin in `S:\Projects\vShell\plugins\<App>`): address + app key in Settings › Plugins; polls `/overview`
  every 5 s while a module shows it, else 30 s; shows events marked `toVShell` as notifications, **never the ones from before it started**
  (it starts from `last`); sends the theme on every change and once the app answers.
- vDeploy also documents every call (`ApiDocs.Calls` -> the `/api` page and `/api/v1/openapi.json`; a test fails on a route that isn't
  listed). The others don't yet.

## 7. Activity and notifications

**Shared since vSuite.Core 1.0.47** (vSuite.Server `Activity.cs`, vSuite.Web `NotificationsOptions`) - never copy them into an app.

- **`ActivityEvent`:** `Id`, `At`, `Kind`, `Title`, `Detail`, `Look` (ok / info / warn / bad), `Group` (its notification group - null =
  only listed), `ToVShell`, `Link` (its page), `HostId` / `Host` (what it's about), `HasBody`. An app with more subclasses it and uses
  `ActivityLog<T>` (vDeploy: `Stack`, `Guest`, `VmId`, `Ok`).
- **`ActivityLog`:** activity.json, newest first, the newest **1,000**; `Write(e, body)` keeps the output of the newest 50 as
  `activity/<id>.log` (`Body(id)`). `Before` (the Notifier marks it) and `Written` (sent) hooks. The app's own `ActivityWatch` turns its
  events into entries: "down" only after a minute (two checks) and only when it starts, "back" only after a "down", a found thing once
  per version.
- **`Notifier`:** `new Notifier("<App>", groups, () => settings)` - `NotifyGroup`s = (key, title, hint, default); each group goes to
  any of **Gotify, Discord and vShell** (`NotifyTo` flags; 1.0.48) - `To(group)` / `Set(group, to)`, stored as "off" or
  "to:gotify,discord,vshell"; a default or an older choice "both" = all three, "gotify" = Gotify and Discord, "vshell", "off".
  `Watch(log)` marks `ToVShell` before saving and sends after: Gotify (a POST to `/message` with `X-Gotify-Key`, title "<App>: …", a
  higher priority for a problem) and Discord (the channel's webhook, from "<App>", red for a problem). The app's settings implement
  `INotifySettings` (`Notify`, `GotifyUrl`, `GotifyToken`, `DiscordWebhook` - secrets `Secrets`-protected).
- **Options › Notifications** is `<NotificationsOptions Notifier="…" Settings="S" Saved="State.Save" Note="vShell shows them with its
  <App> plugin … Everything is also on the Activity page." />`.
- **The side panel** is the app's own, built from `PlaceLink` (Hosts, Activity … with a count) and `SideHead` (a heading with an add
  button) and `Dot`.
- **Defaults:** failures and down / back = both; routine news (updates waiting, a change made outside) = vShell; things you did yourself
  working = off (vDeploy "deployed", vPatch "upgraded", vProxy "changes"); every app has an "<app>" group "A new <App> is out" (once per
  version - `UpdateTold`) = both.
- vMediarr keeps its events in SQLite (`EventLog`, 90 days) but uses the same `Notifier` and Options › Notifications (0.0.31): its
  `AppEvent` is an `INotifyEvent`, its log calls the Notifier's `Mark` before keeping one. Its Radarr / Sonarr-style Connections
  (Webhook, Discord, Gotify, vShell) stay as well, for what came from those apps.

## 8. Data and secrets

- **The data folder** `/var/lib/<app>` (700, the app's user; `PREFIX_DATA_DIR` for tests and Windows) holds everything: settings.json,
  the app's stores, activity.json, `secret.key`, `keys/`, `id_<app>` (its SSH key), `<app>.log`, and the app itself in `app/` (9).
- **Backups** (1.0.51, V: "so if they're ever reinstalled I can just import the backup"): vSuite.Server's `Backups` zips the whole data
  folder - settings, stores, `secret.key`, `keys/`, `id_<app>` (without `secret.key` a restored install can't read a single saved
  password) - less the program (`app/`, `app.new`, `app.old`, `app.trying`, `update-failed`, `update.tar.gz` - never cleared by a restore either), logs, `backups/`, `*.tmp` / `*.part` / `*.sock` and the app's own `Skip`; a note `vsuite-backup.json` (app,
  version, made). Daily (7) + by hand (10) in `backups/`, each copied to `IBackupSettings.BackupCopyTo` when set (the app's newest 7 kept
  there). Plain zips (V): the page says anyone with one can read its passwords. Restore = `restore.zip`, then at the next start
  `Backups.ApplyPending(version)` - **right after `Paths.Init`, before anything reads the data** - keeps a `_before_restore_` backup, clears
  the folder (not backups / logs) and unpacks (never outside it, 600). Refused: another app's, a newer version's. An app with a database
  in use skips its files and writes a safe copy through `AddOwn` (vMediarr: VACUUM INTO); `Legacy` = a file that marks its older backups.
  Program.cs: `var backups = new Backups(AppState.Version, () => state.Settings)`, `BackupWeb.Map(app, backups)`, `backups.RunLoop`.
- **`JsonFile`:** `Save` writes `.tmp`, flushes to disk, keeps the old file as `.bak`, moves over; `Load` keeps an unreadable file as
  `.broken-<time>` and falls back to `.bak`; `must: true` (settings, hosts) stops the app rather than save an empty one over it.
  `Private(path)` = 600. A list edited from several places goes one at a time (a lock or a per-thing gate).
- **`Secrets`:** "vdk1:" = AES-GCM with `secret.key` (32 bytes, 600). Every token, password, API key of another app goes through
  `Protect` / `Unprotect`; app keys and passwords are hashes, not secrets. Moving the folder moves the key with it.
- **Never in a log or an export:** keys, tokens, passwords, cookies, the authenticator secret. vProxy's access logs blank `apikey`,
  `token`, `X-Api-Key`, `Authorization`, cookies and Referer. A hosts export never has a user or key (vDeploy's test checks it).
- **The log:** `Log.Write` -> `<app>.log` (1 MB, then `<app>.old.log`) and the console (journalctl). ASP.NET's own logging filtered to
  warnings. One line per event, errors with their reason.

## 9. Install, update and root

- **`lxc.sh`** (run on a Proxmox node: `bash -c "$(curl -fsSL https://vsuite.vrsion.com/<app>/lxc.sh)"`): asks with defaults (ID = next
  free, name, storage, template storage, cores 1, memory, disk, bridge, dhcp or an address + gateway), root's password typed twice with
  `read -s` (Enter = none - `pct enter` only), set through `chpasswd`'s stdin (never an argument), and whether root may use SSH with it
  (`sshd_config.d/10-root-login.conf`). Makes an **unprivileged Debian 13** LXC (`nesting=1`, `onboot 1`), waits for the network, then
  runs install.sh inside. `PREFIX_YES=1` + `PREFIX_CTID` / `_NAME` / … / `_ROOT_PASSWORD` / `_ROOT_SSH` for no questions. vTrace passes
  the node's time zone; vMediarr asks for a media folder to mount at the same path.
- **`install.sh`** (root, Debian / Ubuntu, x86_64 or arm64): apt `curl ca-certificates tzdata libicu*` (+ what the app needs - vMediarr
  ffmpeg); reads `latest.json`, downloads `<app>-linux-<arch>.tar.gz`, **checks its SHA-256** (`PREFIX_SITE`, `PREFIX_PRE=1`,
  vMediarr `PREFIX_FILE` for a local build); makes the system user; puts the build in `$DATA/app` (the app's own, so it can update
  itself) and `run.sh` in `/opt/<app>/` (root's, CRLF stripped); writes the systemd unit and starts it; ends with "Open http://<ip>:<port>
  to finish setting up." Running it again = update, data kept. Tarballs come from Windows: set modes (`chmod`) after unpacking.
- **The unit:** `User=<app>`, `ExecStart=/bin/bash /opt/<app>/run.sh`, `Environment=PREFIX_DATA_DIR / PREFIX_LAUNCHER=lxc /
  PREFIX_PORT`, `Restart=on-failure`, `NoNewPrivileges`, `ProtectSystem=strict`, `ReadWritePaths=$DATA`, `PrivateTmp`. vProxy adds
  `UMask=0077`; vMediarr uses `ProtectSystem=full` (it writes media folders).
- **Self-update** (`SelfUpdate`, Options › Updates): checks a minute after start, then every 6 h; downloads, checks size + SHA-256,
  unpacks into `app.new`, exits with **code 3** (`ExitForUpdate`; systemd restarts it). `run.sh`: app -> app.old, app.new -> app, writes
  `app.trying`; the app deletes app.trying / app.old after a minute; a start that finds app.trying puts app.old back and writes
  `update-failed`. Only when `PREFIX_LAUNCHER` is set. **No apostrophe inside `${VAR:?message}`** in run.sh; `bash -n` after changes.
- **Root work - vProxy's pattern** (the only app that needs it): the app writes a request into a **shared folder**
  (`/var/lib/vproxy-share`, 2750 app:group, setgid); a root **`.path` unit** (`PathModified=`) starts a oneshot root script in
  `/usr/local/lib/<app>/` that does the one job (put the build's Caddy in place and restart it; start / stop cloudflared) and writes
  what happened back ("<time> updated|back|down <running> <other>" in a status file the app shows). A second program gets its own user
  (`caddy` with only `CAP_NET_BIND_SERVICE`; cloudflared `DynamicUser`) and reads only what it needs from the share. **New root units
  come with install.sh, so existing installs run the install line once more** - the page says so while the script isn't there.
- **Third-party binaries** are pinned and shipped inside the release, never `apt` / self-upgrading: a script fetches the pinned version
  and checks its SHA-256 (vProxy `scripts\caddy.ps1` - Go's SHA-256 too - and `cloudflared.ps1`; vDeploy's xterm.js is in wwwroot with
  its licence, never from a CDN). **When to move:** a security fix = now; otherwise a release once it's **14 days old**, a .0 waits for
  its .1 or 30 days; each bump is a changelog line naming the version. `*-news.ps1` (build.cmd runs them, never failing the build) and
  the running app's daily check (vProxy `PartsNews`) say what's newer. A new binary that won't start is put back and its SHA-256
  remembered (`caddy.refused`) so it isn't tried again.

## 10. Demo mode and tests

- **Demo:** `PREFIX_DEMO=1` with an empty data folder seeds made-up things (`Demo.cs`: `home.example`, 10.0.x addresses, a small
  homelab - one thing down so the pages show a problem) and swaps every outside call for a stand-in (`Fake` on each client, vPatch's
  `HostConnect.Default` = `Demo.Runner` answering each script in the real tool's text). Nothing connects anywhere. vProxy, vDeploy,
  vPatch have it; vTrace writes demo data with `scripts\demo.ps1` and `VTRACE_DEMO=1` reads nothing; vMediarr has none yet.
  **The website's screenshots come from it** (1200 x 760, dark, jpg) - never V's own servers.
- **Trying it on Windows:** `PREFIX_DATA_DIR=<scratch>`, `PREFIX_PORT=84x7`, `ASPNETCORE_ENVIRONMENT=Development`, `dotnet run`; a
  trusted 127.0.0.1 in that settings.json skips sign-in. Pages are checked with **headless Edge over the DevTools protocol** (go /
  evaluate / screenshot; attach to the tab by its address and `Page.bringToFront` first) - never the real mouse or keyboard.
- **Tests:** `tests\<App>.Tests` - **xunit** (vMediarr: NUnit + FluentAssertions 7, as the Sonarr / Radarr code it lifts). Classes that
  touch `Paths` share `[Collection("data folder")]`. Outside APIs get **stand-ins**, never the real service: a fake server or a `PREFIX_*_URL`
  pointing at one (vMediarr `VMEDIARR_TMDB_URL`, a fake Sonarr / Jellyfin), scripts run against a stand-in `docker` / Git's sh. Anything a
  browser posts gets a test against the real server in-process (`WebApplicationFactory<Program>` - vMediarr's `AppFixture`).
- **A test LXC** on nuc01 (vDeploy's CLAUDE.md, "The test setup on nuc01") for releases: the real lxc.sh / install.sh, a test site served
  inside it (`python3 -m http.server`), "safe to delete" in its description - **ask V before making one** (the last was removed).
  V's real servers are read-only unless V asks.

## 11. Build, release, website

- **Repo:** `https://git.vrsion.xyz/Steven/<App>.git` (V's own server, branch `main`), `S:\Projects\<App>`: `src\<App>.Core` (no web),
  `src\<App>` (assembly `<app>`: Program.cs, Api.cs, Services.cs, Components), `tests\<App>.Tests`, `deploy\` (lxc.sh, install.sh,
  run.sh), `scripts\site.ps1`, `build.cmd`, `Directory.Build.props` (`<Version>` 0.1.N), `vSuite.Core` (submodule), README, CLAUDE.md,
  CHANGELOG.md, TESTING.md. Copy the newest app (vProxy) and rename.
- **`build.cmd`:** `suite-core.ps1` (the newest library, committed as `0.1.N: vsuite.core X`), the pinned parts, the tests, then
  `dotnet publish -r linux-x64 / linux-arm64 --self-contained -p:PublishSingleFile=true` with run.sh, version.txt and the parts, as
  `dist\<app>-linux-<arch>.tar.gz`. `build.cmd site` / `pre` = `dist\site\` only; `upload` / `pre upload` puts it on the site.
- **`scripts\site.ps1`:** vSuite.Core's `site-release.ps1` (latest.json, changelog.md, the release notes, the CHANGELOG heading marked
  " · release" / " · pre-release") + the arm64 build, install.sh and lxc.sh (LF endings) added to latest.json's files with size and
  SHA-256; then `site-upload.ps1` (SFTP key `vsuite-site`, latest.json last). **Only when V asks.** Then commit CHANGELOG.md as
  `0.1.N: build (changelog marked release)` and push.
- **House rules** (as the Windows apps): every user-visible change bumps `<Version>`, one short CHANGELOG line (what, not how), test steps
  in TESTING.md; commits `0.1.N: what changed` (lower case); a shared piece changes in vSuite.Core first (with its tests), then
  `git -C vSuite.Core pull` in the app.
- **The website** (vSuite.Site): a section per app as the Windows apps' - tag line, icon, lead, features with demo screenshots, the
  install line, What's new - after V has seen it locally.

## 12. New server app checklist

- [ ] Repo from vProxy's; `vSuite.Core` submodule (+ git's safe.directory); names, PREFIX, port (next 84x0), key prefix changed.
- [ ] `<App>App.Init` = `ServerApp.Init(name, PREFIX, "x_")` (+ a module initializer for the tests), then `Paths.Init`, `AppSettings`
      with `IWebSettings` / `ISelfUpdateSettings`, `Normalize`, `JsonFile` saves (`must` for settings and hosts).
- [ ] Program.cs: Kestrel on the port, cookie (stamp check), data protection in `keys/`, `Gate` (+ `GateRules.First` only if needed),
      `/logout`, `/api/health`, `Api.Map`, `AddAdditionalAssemblies(WebApp.Assembly)`, `WebApp.CardLogo` / `AfterSetup`.
- [ ] Window: `SuiteWindow` (`PlusPath=""`) with the menu and `SidePanel`, the app's `Tabs` (KeyOf / RankOf), StatusLine, phone width.
- [ ] Icon: mock-ups shown to V, then `AppLogo.razor` + favicon.svg; new glyphs in vSuite.Web's `Icon`.
- [ ] Theme: CSS variables only; vsuite.css / vsuite.js linked first; Look page as 4.
- [ ] Options: General · own pages · Connections · Notifications · Sign-in · Look · Backups · Updates · About (with Made with).
- [ ] Backups: settings implement `IBackupSettings`; `Backups.ApplyPending` right after `Paths.Init`; `BackupWeb.Map`, `RunLoop`, the page;
      a test that a backup restores into an empty data folder with its secrets readable.
- [ ] Dangerous actions through `ConfirmDialog`; sign-in changes ask for the password.
- [ ] `ActivityLog` (1,000) + `ActivityWatch` + `Notifier` groups with defaults, an "<app>" update group; Activity page.
- [ ] `/api/v1`: overview with `lastEvent`, events with `toVShell`, `PUT /theme` with WebTheme's names; app keys work.
- [ ] Secrets via `Secrets`, nothing secret in logs or exports; About's data folder line says "Back it up as a whole."
- [ ] deploy\lxc.sh / install.sh / run.sh from vProxy's; the unit hardened; root work only through shared-folder path units.
- [ ] Pinned third-party parts with SHA-256 and their news script; `SelfUpdate` tried on a test LXC (update, and a broken one put back).
- [ ] Demo mode with stand-ins; tests (xunit) for the parts without pages; page checks with headless Edge.
- [ ] build.cmd / site.ps1; CLAUDE.md, README, CHANGELOG `## 0.1.0`, TESTING.md.
- [ ] vShell plugin (`plugins\<App>`: overview, events, theme) and a vTrace source, once the API is settled; website section with demo
      screenshots.
