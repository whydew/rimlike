# Runs the in-game self-test: launches RimWorld with -quicktest -roguelikeSelfTest=<mode>,
# waits for "[RLTEST] DONE", then prints the results. Your mod list is backed up and restored.
#
#   powershell -ExecutionPolicy Bypass -File Tools\selftest.ps1 -Mode win [-OutDir <dir>]
#
# Modes: see SelfTestScripts.For in Source/SelfTest.cs.
param(
    [Parameter(Mandatory = $true)][string]$Mode,
    [string]$OutDir = "",
    [int]$TimeoutSeconds = 600,
    [string]$Scenario = ""
)

$ErrorActionPreference = "Stop"
$game = "C:\Program Files (x86)\Steam\steamapps\common\RimWorld"
$data = Join-Path $env:USERPROFILE "AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios"
$modsConfig = Join-Path $data "Config\ModsConfig.xml"
$log = Join-Path $data "Logs\Player.log"
$shots = Join-Path $data "Screenshots"

if (Get-Process -Name RimWorld -ErrorAction SilentlyContinue) {
    throw "RimWorld is already running; close it first."
}

$backup = "$modsConfig.rltest-backup"
Copy-Item $modsConfig $backup -Force
try {
    [xml]$cfg = Get-Content $modsConfig -Raw
    $active = $cfg.ModsConfigData.activeMods
    $wanted = @("brrainz.harmony", "ludeon.rimworld", "ludeon.rimworld.royalty", "ludeon.rimworld.ideology",
                "ludeon.rimworld.biotech", "ludeon.rimworld.anomaly", "ludeon.rimworld.odyssey", "matt.roguelike")
    $active.RemoveAll()
    foreach ($id in $wanted) { $li = $cfg.CreateElement("li"); $li.InnerText = $id; [void]$active.AppendChild($li) }
    $cfg.Save($modsConfig)

    $started = Get-Date
    $gameArgs = @("-quicktest", "-roguelikeSelfTest=$Mode")
    if ($Scenario) { $gameArgs += "-roguelikeScenario=$Scenario" }
    $p = Start-Process -FilePath (Join-Path $game "RimWorld.exe") -ArgumentList $gameArgs -PassThru
    $done = $false
    while (((Get-Date) - $started).TotalSeconds -lt $TimeoutSeconds) {
        Start-Sleep -Seconds 3
        if ((Test-Path $log) -and (Get-Item $log).LastWriteTime -gt $started) {
            if (Select-String -Path $log -Pattern "\[RLTEST\] DONE" -Quiet) { $done = $true; break }
        }
        if ($p.HasExited) { break }
    }
    Start-Sleep -Seconds 5
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
}
finally {
    Copy-Item $backup $modsConfig -Force
    Remove-Item $backup -Force
}

Write-Output "---- result (done=$done) ----"
Select-String -Path $log -Pattern "\[RLTEST\]" | ForEach-Object { $_.Line }
Write-Output "---- errors in log ----"
Select-String -Path $log -Pattern " ERROR " -Context 0, 6 | ForEach-Object { $_.Line; $_.Context.PostContext }

if ($OutDir) {
    New-Item -ItemType Directory -Force $OutDir | Out-Null
    Copy-Item $log (Join-Path $OutDir "Player-$Mode.log") -Force
    Get-ChildItem $shots -Filter "rltest_*.png" -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -gt $started } |
        ForEach-Object { Move-Item $_.FullName (Join-Path $OutDir $_.Name) -Force }
}
