<#
  Every app's build.cmd runs this first: brings the app's vSuite.Core submodule up to the newest library on its main branch, so a build
  always has it (before 1.0.26 only vShell's build did - the others drifted onto older libraries). If the library moved, the new
  submodule commit is committed in the app as "<app version>: vsuite.core X", so the release matches git (not pushed).
  No network, or changes of your own in vSuite.Core: it says so and the app builds with the library as it is.
  -App  the app's folder (default: the folder above vSuite.Core, i.e. the app this copy of the library is in)
#>
param([string]$App = '')
$ErrorActionPreference = 'Stop'
if ($App -eq '') { $App = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent }
Set-Location $App
$sub = 'vSuite.Core'

function Ver($file) {
    $m = Select-String -Path $file -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
    if ($m) { return $m.Matches[0].Groups[1].Value } else { return '?' }
}

# the app's own project: <folder name>.csproj, else the only .csproj in the folder
$name = Split-Path $App -Leaf
$proj = Join-Path $App "$name.csproj"
if (-not (Test-Path $proj)) { $proj = (Get-ChildItem $App -Filter *.csproj | Select-Object -First 1).FullName }
# the server apps (vDeploy, vPatch) keep their version in Directory.Build.props, their projects under src\
if (-not $proj -or -not (Test-Path $proj)) { $proj = Join-Path $App 'Directory.Build.props' }

if (-not (Test-Path "$sub\.git")) {
    Write-Host 'vSuite.Core: fetching the submodule (first time)...'
    git submodule update --init $sub
    if ($LASTEXITCODE -ne 0) { Write-Host 'vSuite.Core: could not fetch it - build stopped.'; exit 1 }
}

if (git -C $sub status --porcelain) {
    Write-Host 'vSuite.Core: it has changes of its own (not pushed to the library) - left as it is.'
    exit 0
}

$before = (git -C $sub rev-parse HEAD).Trim()
git -C $sub fetch -q origin
if ($LASTEXITCODE -ne 0) {
    Write-Host "vSuite.Core: couldn't reach the library - building with $(Ver "$sub\vSuite.Core\vSuite.Core.csproj") as it is."
    exit 0
}
git -C $sub checkout -q main
git -C $sub merge -q --ff-only origin/main
if ($LASTEXITCODE -ne 0) {
    Write-Host 'vSuite.Core: its main branch has moved away from the library''s (not a straight update) - left as it is.'
    exit 0
}
$after = (git -C $sub rev-parse HEAD).Trim()
$lib = Ver "$sub\vSuite.Core\vSuite.Core.csproj"

# the commit the app points at, compared with what's checked out now
$pinned = ((git ls-tree HEAD $sub) -split '\s+')[2]
if ($after -eq $pinned) {
    Write-Host "vSuite.Core: $lib - up to date."
    exit 0
}

if ($before -ne $after) { Write-Host "vSuite.Core: updated to $lib." }
$ver = Ver $proj
git add $sub
git commit -q -m "${ver}: vsuite.core $lib" -- $sub
if ($LASTEXITCODE -ne 0) { Write-Host 'vSuite.Core: could not commit the update - build stopped.'; exit 1 }
Write-Host "vSuite.Core: committed as `"${ver}: vsuite.core $lib`" (not pushed)."
exit 0
