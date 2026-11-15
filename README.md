# vSuite.Core

The library shared by the vSuite apps for Windows 11 - vShell, vXplorer, vView, vTerm, vPad and vTrace - and by the server apps
vDeploy and vPatch: the theme that follows vShell's, the window glass, styles and controls, questions, the Options window, the log,
single instance, and updates from https://vsuite.vrsion.com.

- `vSuite.Core` - plain .NET and WPF (namespaces `vSuite`, `vSuite.Ui`), no packages.
- `vSuite.Shell` - Windows Shell icons and thumbnails (Vanara).
- `vSuite.Server` and `vSuite.Web` - the server apps' sign-in, SSH and pages (SSH.NET, Razor).
- `vSuite.Tests` - `dotnet test vSuite.Tests`.
- `docs\design-guide.md` - how a vSuite app looks, behaves and is put together.

Each app has this repo as a git submodule in its own folder (`<app>\vSuite.Core`, `git submodule update --init`) and builds it into
its own exe.

## Licence

Copyright (c) 2026 Steven (version-au). MIT - see [LICENSE](LICENSE). The parts by others are listed in
[THIRD-PARTY.txt](THIRD-PARTY.txt). The apps that use it are GPL-3.0: https://github.com/version-au