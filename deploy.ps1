<#
    Deploys the built mod into the Mod Organizer 2 mods folder.

    Only runtime files are copied: the plugin, the compiled scripts, the SKSE DLL
    and the PrismaUI view. Sources and build output stay in the repository.

    Run after: dotnet run (ESP), PapyrusCompiler (pex), cmake --build (dll).
#>
[CmdletBinding()]
param(
    [string]$ModsRoot = 'C:\TAKEALOOK\mods',
    [string]$ModName  = 'BankPrismUI'
)

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
$dest = Join-Path $ModsRoot $ModName

$files = @(
    @{ From = 'BankPrismUI.esp';                              To = 'BankPrismUI.esp' }
    @{ From = 'Scripts\BankPrismController.pex';              To = 'Scripts\BankPrismController.pex' }
    @{ From = 'Scripts\BankPrismDialogueFragment.pex';        To = 'Scripts\BankPrismDialogueFragment.pex' }
    @{ From = 'Scripts\BankPrismNative.pex';                  To = 'Scripts\BankPrismNative.pex' }
    @{ From = 'Scripts\Source\BankPrismController.psc';       To = 'Scripts\Source\BankPrismController.psc' }
    @{ From = 'Scripts\Source\BankPrismDialogueFragment.psc'; To = 'Scripts\Source\BankPrismDialogueFragment.psc' }
    @{ From = 'Scripts\Source\BankPrismNative.psc';           To = 'Scripts\Source\BankPrismNative.psc' }
    @{ From = 'SKSE\Plugins\BankPrismNative.dll';             To = 'SKSE\Plugins\BankPrismNative.dll' }
    @{ From = 'PrismaUI\views\BankPrism\BankView.html';       To = 'PrismaUI\views\BankPrism\BankView.html' }
)

$missing = $files | Where-Object { -not (Test-Path (Join-Path $repo $_.From)) }
if ($missing) {
    foreach ($m in $missing) { Write-Host "MISSING: $($m.From)" }
    throw "$($missing.Count) source file(s) missing - build them before deploying."
}

foreach ($f in $files) {
    $src = Join-Path $repo $f.From
    $dst = Join-Path $dest $f.To
    $dir = Split-Path $dst -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    Copy-Item -Path $src -Destination $dst -Force
    Write-Host "  $($f.To)"
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
