<#
    Deploys the built mod into the Mod Organizer 2 mods folder.

    Only runtime files are copied: the plugin, the compiled scripts, the SKSE DLL
    and the PrismaUI view. Sources and build output stay in the repository.

    Run after: dotnet run (ESP), PapyrusCompiler (pex), cmake --build (dll).

    This writes into mods\<ModName> directly, without going through the MO2 UI, so
    the CRDW Auto Mode plugin never sees a reason to rebuild its directory cache.
    That cache holds the file index for Data\Scripts among others - not file
    contents - so overwriting an existing .pex is safe, but a .pex that is new or
    renamed would be missing from the index and the engine would never learn it
    exists. No error, no log line, just a script that is silently not there. The
    CRDW section at the bottom deals with exactly that case and nothing else.
#>
[CmdletBinding()]
param(
    [string]$ModsRoot = 'C:\TAKEALOOK\mods',
    [string]$ModName  = 'BankPrismUI',
    # Where ModOrganizer.ini and profiles\ live. Only used to find the CRDW cache.
    [string]$Mo2Root  = 'C:\TAKEALOOK',
    # Papyrus sources are not read at runtime; they ship only if asked for.
    [switch]$IncludeSources
)

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
$dest = Join-Path $ModsRoot $ModName

# Fixed single files. Scripts are enumerated instead of listed, so a newly added
# fragment is deployed without anyone remembering to edit this script.
$files = @(
    'BankPrismUI.esp'
    'SKSE\Plugins\BankPrismNative.dll'
)

# The whole view folder, so background art and any other asset travel with the page.
$viewRoot = Join-Path $repo 'PrismaUI\views\BankPrism'
foreach ($f in Get-ChildItem -Path $viewRoot -File -Recurse) {
    $files += $f.FullName.Substring($repo.Length + 1)
}

$found = Get-ChildItem -Path (Join-Path $repo 'Scripts\*.pex') -File -ErrorAction SilentlyContinue
if (-not $found) { throw 'No files matched Scripts\*.pex - build before deploying.' }
foreach ($f in $found) { $files += $f.FullName.Substring($repo.Length + 1) }

# .psc is never read by the game. Shipping it only widens the directory index CRDW
# has to walk and adds names that can go stale, so it is off by default; pass
# -IncludeSources to put the sources back, which is what a public release would
# conventionally do. Recompiling reads the repository, not this copy, so leaving
# them out breaks nothing in this project's own workflow.
if ($IncludeSources) {
    $srcFound = Get-ChildItem -Path (Join-Path $repo 'Scripts\Source\*.psc') -File -ErrorAction SilentlyContinue
    if (-not $srcFound) { throw 'No files matched Scripts\Source\*.psc.' }
    foreach ($f in $srcFound) { $files += $f.FullName.Substring($repo.Length + 1) }
}

$missing = $files | Where-Object { -not (Test-Path (Join-Path $repo $_)) }
if ($missing) {
    foreach ($m in $missing) { Write-Host "MISSING: $m" }
    throw "$($missing.Count) source file(s) missing - build them before deploying."
}

# Whether the set of paths under Scripts\ changed, as opposed to their contents.
# Only a change to the set can leave the cache wrong.
$scriptIndexChanged = $false
$deployedPex = @()
$scriptsPrefix = 'Scripts' + [IO.Path]::DirectorySeparatorChar

foreach ($rel in $files) {
    $src = Join-Path $repo $rel
    $dst = Join-Path $dest $rel
    $dir = Split-Path $dst -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    $isNew = -not (Test-Path -LiteralPath $dst)
    Copy-Item -Path $src -Destination $dst -Force

    if ($rel.StartsWith($scriptsPrefix)) {
        if ($isNew) {
            $scriptIndexChanged = $true
            Write-Host "  $rel   (new file)"
        } else {
            Write-Host "  $rel"
        }
        if ($rel -like '*.pex') { $deployedPex += $rel.Substring($scriptsPrefix.Length) }
    } else {
        Write-Host "  $rel"
    }
}

# Anything in the deployed copy that is no longer in the repository is left over
# from an earlier build. Renamed art in particular would otherwise sit there
# forever - the Korean emblem and portrait filenames survived their own rename
# this way - and a stale .pex keeps a name in the index that nothing produces.
function Remove-Stale {
    param([string]$DeployedRoot, [hashtable]$Keep, [string]$Label)
    $removedAny = $false
    if (-not (Test-Path $DeployedRoot)) { return $false }
    foreach ($f in Get-ChildItem -Path $DeployedRoot -File -Recurse) {
        $rel = $f.FullName.Substring($DeployedRoot.Length + 1)
        if (-not $Keep.ContainsKey($rel)) {
            Remove-Item -LiteralPath $f.FullName -Force
            Write-Host "  removed stale ($Label): $rel"
            $removedAny = $true
        }
    }
    # Folders emptied by the sweep above, deepest first so parents can go too.
    # Dropping Scripts\Source leaves one of these behind every time otherwise.
    foreach ($d in Get-ChildItem -Path $DeployedRoot -Directory -Recurse |
                   Sort-Object { $_.FullName.Length } -Descending) {
        if (-not (Get-ChildItem -LiteralPath $d.FullName -Force)) {
            Remove-Item -LiteralPath $d.FullName -Force
            Write-Host "  removed empty ($Label): $($d.FullName.Substring($DeployedRoot.Length + 1))"
        }
    }
    return $removedAny
}

$keepView = @{}
foreach ($f in Get-ChildItem -Path $viewRoot -File -Recurse) {
    $keepView[$f.FullName.Substring($viewRoot.Length + 1)] = $true
}
$deployedView = Join-Path $dest 'PrismaUI\views\BankPrism'
[void](Remove-Stale -DeployedRoot $deployedView -Keep $keepView -Label 'view')

$keepScripts = @{}
foreach ($rel in $files) {
    if ($rel.StartsWith($scriptsPrefix)) { $keepScripts[$rel.Substring($scriptsPrefix.Length)] = $true }
}
$deployedScripts = Join-Path $dest 'Scripts'
if (Remove-Stale -DeployedRoot $deployedScripts -Keep $keepScripts -Label 'scripts') {
    $scriptIndexChanged = $true
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

# ---- CRDW directory cache -------------------------------------------------
# Deleting data_scripts_.cache is the one lever that works from outside: the Auto
# Mode plugin checks its required cache files on every run and marks the profile
# dirty if any is missing, which puts the next launch in CACHE mode. Editing
# state.json does not work - MO2 holds that state in memory and writes over it.
#
# Invalidation costs a full rebuild of the cache, so it happens only when the set
# of script paths actually changed. Rewriting an existing .pex leaves the index
# correct and is left alone.

$crdwStatus = 'skipped'
$cacheFile = $null

$iniPath = Join-Path $Mo2Root 'ModOrganizer.ini'
if (Test-Path $iniPath) {
    $line = Select-String -Path $iniPath -Pattern '^selected_profile=' | Select-Object -First 1
    if ($line) {
        $value = $line.Line -replace '^selected_profile=', ''
        if ($value -match '^@ByteArray\((.*)\)$') { $profileName = $Matches[1] } else { $profileName = $value }
        $cacheDir = Join-Path $Mo2Root (Join-Path 'profiles' (Join-Path $profileName 'CRDW Automatic Mode\cache'))
        if (Test-Path $cacheDir) { $cacheFile = Join-Path $cacheDir 'data_scripts_.cache' }
    }
}

Write-Host ''
if (-not $cacheFile) {
    Write-Host 'CRDW: not installed for this profile - nothing to invalidate.'
} elseif ($scriptIndexChanged) {
    if (Test-Path $cacheFile) {
        Remove-Item -LiteralPath $cacheFile -Force
        Write-Host 'CRDW: script path set changed - invalidated data_scripts_.cache'
    } else {
        Write-Host 'CRDW: script path set changed - cache was already absent.'
    }
    Write-Host '      The next run rebuilds it in CACHE mode; the new scripts register then.'
    $crdwStatus = 'invalidated'
} elseif (-not (Test-Path $cacheFile)) {
    Write-Host 'CRDW: cache absent - the next run rebuilds it in CACHE mode.'
    $crdwStatus = 'absent'
} else {
    Write-Host 'CRDW: script paths unchanged - cache kept.'
    $crdwStatus = 'kept'
}

# ---- Verify the game can actually see what was deployed -------------------
# "Built and deployed" is not "the game will load it". This is the gap where that
# stops being true, so it is checked here rather than found in-game.
if ($crdwStatus -eq 'kept') {
    $bytes = [IO.File]::ReadAllBytes($cacheFile)
    $index = [Text.Encoding]::GetEncoding(28591).GetString($bytes)
    $unregistered = @()
    foreach ($rel in $deployedPex) {
        $needle = 'data\SCRIPTS\' + $rel
        if ($index.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
            $unregistered += $rel
        }
    }
    if ($unregistered.Count -gt 0) {
        Write-Host ''
        foreach ($u in $unregistered) { Write-Host "UNREGISTERED: Scripts\$u" }
        throw ("$($unregistered.Count) deployed script(s) are absent from the CRDW index. " +
               'The game would not see them. Delete the cache file and run again.')
    }
    Write-Host "      verified $($deployedPex.Count) script(s) present in the CRDW index."
}

Write-Host ''
Write-Host "Deployed to: $dest"
