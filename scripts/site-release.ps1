<#
  The update site's files for one app's release - each app's build.cmd calls this (vSuite.Core\scripts\site-release.ps1).

  Writes <Out>\<app>\ (or <Out>\<app>\pre\ with -Pre): the file the updater installs, the installer, and latest.json - the version, the
  release notes and each file's size and SHA-256 (what vSuite.Updater reads; see Updater.cs). Copy <Out>\ over the site's root: only the
  newest release is kept there, each new one overwrites the files of the one before.

  -Install      the file the updater installs (dist\vShell.exe, dist\release\vView-0.1.17.zip)
  -InstallName  its name on the site (default: its own name) - vView's zip goes up as vView.zip, so it overwrites the last one
  -Installer    the setup exe (optional)
  -Notes        the release notes (dist\release-notes.md)
  -Changelog    the app's CHANGELOG.md - goes up as changelog.md: the update notice lists every version's changes since yours
  -Extra        more values for latest.json, as JSON ('{"plugin_api":2}')
#>
param(
  [Parameter(Mandatory = $true)][string]$App,
  [Parameter(Mandatory = $true)][string]$Version,
  [Parameter(Mandatory = $true)][string]$Install,
  [string]$InstallName = '',
  [string]$Installer = '',
  [string]$Notes = '',
  [string]$Changelog = '',
  [Parameter(Mandatory = $true)][string]$Out,
  [switch]$Pre,
  [string]$Extra = ''
)
$ErrorActionPreference = 'Stop'

$folder = Join-Path $Out $App.ToLowerInvariant()
if ($Pre) { $folder = Join-Path $folder 'pre' }
New-Item -ItemType Directory -Force $folder | Out-Null
# this release's own files only (a pre\ or plugins\ folder inside stays)
Get-ChildItem $folder -File | Remove-Item -Force

function Put($src, $name) {
  Copy-Item $src (Join-Path $folder $name) -Force
  $f = Get-Item (Join-Path $folder $name)
  return [ordered]@{ size = $f.Length; sha256 = (Get-FileHash -Algorithm SHA256 $f.FullName).Hash.ToLowerInvariant() }
}

if (-not (Test-Path $Install)) { throw "$Install isn't there" }
if ($InstallName -eq '') { $InstallName = Split-Path $Install -Leaf }
$files = [ordered]@{}
$files[$InstallName] = Put $Install $InstallName
$setupName = ''
if ($Installer -ne '' -and (Test-Path $Installer)) {
  $setupName = Split-Path $Installer -Leaf
  $files[$setupName] = Put $Installer $setupName
}

$text = ''
if ($Notes -ne '' -and (Test-Path $Notes)) { $text = [IO.File]::ReadAllText((Resolve-Path $Notes).Path, [Text.Encoding]::UTF8).Trim() }

$latest = [ordered]@{
  app = $App
  version = $Version
  published = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
  prerelease = [bool]$Pre
  notes = $text
  install = $InstallName
}
if ($setupName -ne '') { $latest['installer'] = $setupName }
if ($Extra -ne '') { (ConvertFrom-Json $Extra).PSObject.Properties | ForEach-Object { $latest[$_.Name] = $_.Value } }
$latest['files'] = $files
if ($Changelog -ne '' -and (Test-Path $Changelog)) {
  # Mark this version's heading in the app's CHANGELOG.md as a pre-release or a release ("## 0.1.4 · release"), so the changelog - and
  # the update notice, which shows the headings - says which builds were which. A release replaces an earlier pre-release mark.
  # The app's CHANGELOG.md changes: commit it after the build. (The dot is [char]0xB7: Windows PowerShell reads this file as ANSI.)
  $dot = [string][char]0xB7
  $path = (Resolve-Path $Changelog).Path
  $log = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
  $mark = if ($Pre) { " $dot pre-release" } else { " $dot release" }
  $rx = New-Object Text.RegularExpressions.Regex ('(?m)^(## v?' + [Text.RegularExpressions.Regex]::Escape($Version) + ')(?=[ \t\r]|$)([^\r\n]*?)( ' + $dot + ' (pre-)?release)?([ \t]*)(\r?)$')
  $m = $rx.Match($log)
  if ($m.Success) {
    $line = $m.Groups[1].Value + $m.Groups[2].Value + $mark + $m.Groups[6].Value
    if ($line -ne $m.Value) {
      $log = $log.Substring(0, $m.Index) + $line + $log.Substring($m.Index + $m.Length)
      [IO.File]::WriteAllText($path, $log, (New-Object Text.UTF8Encoding($false)))
      Write-Host "Changelog: $Version marked as$mark (commit $Changelog)"
    }
  }
  else { Write-Host "Changelog: no '## $Version' heading to mark" -ForegroundColor Yellow }
  Copy-Item $Changelog (Join-Path $folder 'changelog.md') -Force
}
[IO.File]::WriteAllText((Join-Path $folder 'latest.json'), (ConvertTo-Json $latest -Depth 6), (New-Object Text.UTF8Encoding($false)))

Write-Host "Update site: $folder ($App $Version$(if ($Pre) { ', pre-release' }))"
