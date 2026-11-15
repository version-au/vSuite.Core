<#
  Puts an app's built site files (dist\site\, from site-release.ps1) on the update site by SFTP - each app's "build.cmd upload".
  Windows' own sftp.exe, signed in with a key: the "vsuite-site" entry in %USERPROFILE%\.ssh\config (HostName, User, IdentityFile).
  - Every file goes up as <name>.part and is then renamed over the old one (the server's rename replaces it in one step), so someone
    downloading meanwhile gets the old file whole, never half of the new one.
  - Each latest.json goes up last: a new version is only offered once all its files are there.
  - Nothing on the site is deleted (other apps, the pre\ folder, older plugins stay).
  - Then each latest.json is read back over https: the version, and every file it lists there with the right size.
  -Site    the folder to put up (dist\site) - what's inside it goes into -Root
  -Target  the ssh config entry (default vsuite-site); -Root the site's folder on the server (default /www)
  -Url     the site's address for the check (default https://vsuite.vrsion.com)
  Exit 1 when something failed (if it stopped before a latest.json, that app's old version is still the one offered).
#>
param(
  [Parameter(Mandatory = $true)][string]$Site,
  [string]$Target = 'vsuite-site',
  [string]$Root = '/www',
  [string]$Url = 'https://vsuite.vrsion.com'
)
$ErrorActionPreference = 'Stop'

if (-not (Get-Command sftp.exe -ErrorAction SilentlyContinue)) { Write-Host "Windows' OpenSSH client (sftp.exe) isn't installed."; exit 1 }
if (-not (Test-Path $Site)) { Write-Host "$Site isn't there - build first."; exit 1 }
$Site = (Resolve-Path $Site).Path.TrimEnd('\')
$files = @(Get-ChildItem $Site -Recurse -File)
if ($files.Count -eq 0) { Write-Host "$Site is empty - build first."; exit 1 }

function Remote($item) { return $Root.TrimEnd('/') + '/' + $item.FullName.Substring($Site.Length + 1).Replace('\', '/') }
function Q($s) { return '"' + $s + '"' }

# the commands: folders first ("-": already there is fine), then each file as .part + rename, the latest.json files last
$cmds = New-Object System.Collections.Generic.List[string]
foreach ($d in (Get-ChildItem $Site -Recurse -Directory | Sort-Object { $_.FullName.Length })) { $cmds.Add('-mkdir ' + (Q (Remote $d))) }
$last = @($files | Where-Object { $_.Name -eq 'latest.json' })
$rest = @($files | Where-Object { $_.Name -ne 'latest.json' })
foreach ($f in ($rest + $last)) {
  $r = Remote $f
  $cmds.Add('put ' + (Q $f.FullName.Replace('\', '/')) + ' ' + (Q "$r.part"))
  $cmds.Add('rename ' + (Q "$r.part") + ' ' + (Q $r))
}
$batch = Join-Path ([IO.Path]::GetTempPath()) ('site-upload-' + [Guid]::NewGuid().ToString('N') + '.txt')
[IO.File]::WriteAllLines($batch, $cmds.ToArray(), (New-Object Text.UTF8Encoding($false)))

$mb = [Math]::Round((($files | Measure-Object Length -Sum).Sum) / 1MB, 1)
Write-Host "Uploading $($files.Count) files ($mb MB) from $Site to ${Target}:$Root ..."
$sw = [Diagnostics.Stopwatch]::StartNew()
# the progress lines sftp prints go to the console; "sftp> put …" lines show what it's on
& sftp.exe -o BatchMode=yes -o ConnectTimeout=20 -b $batch $Target
$code = $LASTEXITCODE
Remove-Item $batch -ErrorAction SilentlyContinue
if ($code -ne 0) {
  Write-Host ''
  Write-Host "UPLOAD STOPPED (sftp exit $code) - see above. Apps whose latest.json didn't go up still offer their old version." -ForegroundColor Red
  exit 1
}
Write-Host ("Uploaded in {0:0}s." -f $sw.Elapsed.TotalSeconds)

# the check: each latest.json from the site, and every file it lists, with its size
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$bad = 0
foreach ($f in $last) {
  $rel = $f.FullName.Substring($Site.Length + 1).Replace('\', '/')
  $local = Get-Content -Raw $f.FullName | ConvertFrom-Json
  $folder = $Url.TrimEnd('/') + '/' + $rel.Substring(0, $rel.LastIndexOf('/') + 1)
  try {
    # read as bytes -> UTF-8 (with or without a BOM): Invoke-RestMethod in Windows PowerShell can't parse a file that starts with one
    $resp = Invoke-WebRequest -UseBasicParsing -Headers @{ 'Cache-Control' = 'no-cache' } -Uri ($Url.TrimEnd('/') + '/' + $rel + '?t=' + [DateTime]::UtcNow.Ticks)
    $live = [Text.Encoding]::UTF8.GetString($resp.RawContentStream.ToArray()).TrimStart([char]0xFEFF) | ConvertFrom-Json
  } catch { Write-Host "  $rel - couldn't read it from the site: $($_.Exception.Message)" -ForegroundColor Red; $bad++; continue }
  if ("$($live.version)" -ne "$($local.version)") { Write-Host "  $rel - the site says $($live.version), expected $($local.version)" -ForegroundColor Red; $bad++; continue }
  $ok = $true
  foreach ($p in $local.files.PSObject.Properties) {
    try {
      $h = Invoke-WebRequest -UseBasicParsing -Method Head -Uri ($folder + [Uri]::EscapeDataString($p.Name))
      if ([long]$h.Headers['Content-Length'] -ne [long]$p.Value.size) { Write-Host "  $($folder)$($p.Name) - $($h.Headers['Content-Length']) bytes on the site, expected $($p.Value.size)" -ForegroundColor Red; $ok = $false }
    } catch { Write-Host "  $($folder)$($p.Name) - not reachable: $($_.Exception.Message)" -ForegroundColor Red; $ok = $false }
  }
  if ($ok) { Write-Host "  $($local.app) $($local.version)$(if ($local.prerelease) { ' (pre-release)' }) is live: $($folder)latest.json" -ForegroundColor Green } else { $bad++ }
}
if ($bad -gt 0) { Write-Host "The site check found $bad problem(s) - see above." -ForegroundColor Red; exit 1 }
exit 0
