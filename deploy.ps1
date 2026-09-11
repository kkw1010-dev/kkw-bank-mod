<#
    Deploys the built mod into the Mod Organizer 2 mods folder.

    Only runtime files are copied: the plugin, the compiled scripts, the SKSE DLL
    and the PrismaUI view. Sources and build output stay in the repository.

    Run after: dotnet run (ESP), PapyrusCompiler (pex), cmake --build (dll).
#>
[CmdletBinding()]
param(
    [string]$ModsRoot = 'C:\TAKEALOOK\mods',
    [string]$ModName  = 'Financial Hell - Skyrim Banking and Credit Overhaul_dev build'
)

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
$dest = Join-Path $ModsRoot $ModName

# Fixed single files. Scripts are enumerated instead of listed, so a newly added
# fragment is deployed without anyone remembering to edit this script.
$files = @(
    'BankPrismUI.esp'
    # Written by the generator beside the plugin. Without it the steward topic is missing
    # from any game started straight after launching Skyrim - see WriteAndCheckSeq.
    'SEQ\BankPrismUI.seq'
    'SKSE\Plugins\BankPrismNative.dll'
)

# The whole view folder, so background art and any other asset travel with the page.
$viewRoot = Join-Path $repo 'PrismaUI\views\BankPrism'
foreach ($f in Get-ChildItem -Path $viewRoot -File -Recurse) {
    $files += $f.FullName.Substring($repo.Length + 1)
}

foreach ($pattern in @('Scripts\*.pex', 'Scripts\Source\*.psc')) {
    $found = Get-ChildItem -Path (Join-Path $repo $pattern) -File -ErrorAction SilentlyContinue
    if (-not $found) { throw "No files matched $pattern - build before deploying." }
    foreach ($f in $found) {
        $files += $f.FullName.Substring($repo.Length + 1)
    }
}

$missing = $files | Where-Object { -not (Test-Path (Join-Path $repo $_)) }
if ($missing) {
    foreach ($m in $missing) { Write-Host "MISSING: $m" }
    throw "$($missing.Count) source file(s) missing - build them before deploying."
}

foreach ($rel in $files) {
    $src = Join-Path $repo $rel
    $dst = Join-Path $dest $rel
    $dir = Split-Path $dst -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    Copy-Item -Path $src -Destination $dst -Force
    Write-Host "  $rel"
}

# Read every copied file back against its source. A copy that did not land - a file held
# open by MO2 or the game - otherwise shows up only as the game quietly running the last
# build, which is found in game or not at all.
$differs = @($files | Where-Object {
    (Get-FileHash -LiteralPath (Join-Path $repo $_)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $dest $_)).Hash
})
if ($differs.Count -gt 0) {
    foreach ($d in $differs) { Write-Host "NOT DEPLOYED: $d" }
    throw "$($differs.Count) file(s) differ from the repository after copying."
}
Write-Host "  verified: all $($files.Count) files identical to the repository"

# The view folder is generated entirely from the repository, so anything in the
# deployed copy that is no longer in the repository is left over from an earlier
# build. Renamed art in particular would otherwise sit there forever - the Korean
# emblem and portrait filenames survived their own rename this way.
$deployedView = Join-Path $dest ('PrismaUI' + [IO.Path]::DirectorySeparatorChar + 'views' + [IO.Path]::DirectorySeparatorChar + 'BankPrism')
if (Test-Path $deployedView) {
    $keep = @{}
    foreach ($f in Get-ChildItem -Path $viewRoot -File -Recurse) {
        $keep[$f.FullName.Substring($viewRoot.Length + 1)] = $true
    }
    foreach ($f in Get-ChildItem -Path $deployedView -File -Recurse) {
        $rel = $f.FullName.Substring($deployedView.Length + 1)
        if (-not $keep.ContainsKey($rel)) {
            Remove-Item -LiteralPath $f.FullName -Force
            Write-Host "  removed stale (view): $rel"
        }
    }
}

$meta = Join-Path $dest 'meta.ini'
if (-not (Test-Path $meta)) {
    @(
        '[General]'
        'modid=0'
        'version=1.0.0'
        'newestVersion='
        'category="0"'
        'installationFile='
        'notes="Bank and merchant credit system. Built from C:\TAKEALOOK\BankPrismUI."'
    ) | Out-File -FilePath $meta -Encoding utf8
    Write-Host '  meta.ini'
}

Write-Host ''
Write-Host "Deployed to: $dest"
