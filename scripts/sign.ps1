<#
  Signs the Windows apps' files with the developer's Certum code-signing certificate - each app's build.cmd, and Inno Setup (setup +
  uninstaller) through sign.cmd. The certificate lives in Certum's cloud: SimplySign Desktop must be connected (a code from the phone),
  then it's in the user's certificate store and signtool can use it.
  - The certificate: the newest valid one in Cert:\CurrentUser\My for Code Signing issued by Certum, with its private key - or the
    thumbprint in %VSUITE_SIGN_THUMBPRINT%.
  - SHA-256, timestamped by Certum (http://time.certum.pl, tried 3 times), so signatures stay valid after the certificate expires; each
    file is checked after (signtool verify /pa).
  -Files    the files to sign
  -Require  no certificate = exit 1 ("connect SimplySign"); without it = a warning and exit 2 (nothing signed - the build goes on)
  Exit 0 signed, 1 failed, 2 skipped (no certificate, not required).
#>
param(
  [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)][string[]]$Files,
  [switch]$Require
)
$ErrorActionPreference = 'Stop'

# signtool: the newest Windows SDK's
$kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$signtool = Get-ChildItem $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\x64\\' } |
  Sort-Object { [version]($_.Directory.Parent.Name -replace '[^0-9.]', '0') } -Descending | Select-Object -First 1
if (-not $signtool) {
  Write-Host "signtool.exe not found - install the Windows SDK's signing tools (Visual Studio Installer, or the Windows SDK)."
  if ($Require) { exit 1 } else { exit 2 }
}

$now = Get-Date
$cert = $null
if ($env:VSUITE_SIGN_THUMBPRINT) {
  $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Thumbprint -eq $env:VSUITE_SIGN_THUMBPRINT.Trim() } | Select-Object -First 1
} else {
  $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object {
    $_.HasPrivateKey -and $_.Issuer -match 'Certum' -and $_.NotBefore -le $now -and $_.NotAfter -gt $now -and
    ($_.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' })
  } | Sort-Object NotAfter -Descending | Select-Object -First 1
}
if (-not $cert) {
  Write-Host "No code-signing certificate - connect SimplySign Desktop first (its tray icon > Connect, the code from your phone)."
  if ($Require) { Write-Host "(Or build with nosign for an unsigned build that isn't uploaded.)"; exit 1 }
  Write-Host "Building unsigned."
  exit 2
}

$failed = $false
foreach ($f in $Files) {
  $f = $f.Trim('"')
  if (-not (Test-Path $f)) { Write-Host "Sign: $f isn't there."; $failed = $true; continue }
  $done = $false
  for ($try = 1; $try -le 3 -and -not $done; $try++) {
    & $signtool.FullName sign /sha1 $cert.Thumbprint /fd sha256 /tr http://time.certum.pl /td sha256 /q $f
    if ($LASTEXITCODE -eq 0) { $done = $true } else { Start-Sleep -Seconds (3 * $try) }
  }
  if (-not $done) { Write-Host "Sign: couldn't sign $f (is SimplySign Desktop still connected?)"; $failed = $true; continue }
  & $signtool.FullName verify /pa /q $f | Out-Null
  if ($LASTEXITCODE -ne 0) { Write-Host "Sign: $f was signed but doesn't check out."; $failed = $true; continue }
  Write-Host ("Signed: " + (Split-Path $f -Leaf))
}
if ($failed) { exit 1 }
exit 0
