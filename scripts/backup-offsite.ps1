<#
.SYNOPSIS
  backup-offsite.ps1 — IBM 3-2-1 rule "1 off-site" copy for CCL-MES (Windows).

.DESCRIPTION
  Windows counterpart of scripts/backup-offsite.sh. Picks the newest local
  backup artifacts and copies them to an off-site target (external disk,
  mapped drive, or UNC share \\nas\share) — meant to run from Task
  Scheduler AFTER the in-process BackupSchedulerService finishes (~02:30).

  CCL-MES backup layout:
    <DATA_DIR>\Backup\SQLite\ccl_mes.db.bak.snapshot-*   (online snapshot)
    <DATA_DIR>\Backup\Blobs\blobs_YYYYMMDD.tar.gz        (drawings/CAD tarball)

  Configuration via environment variables (or pass -DataDir / -Target):
    MES_DATA_DIR        C:\ccl-mes\data                  (required)
    MES_OFFSITE_TARGET  D:\ccl-mes-backup  OR  \\nas\backup\ccl-mes   (required)
    MES_OFFSITE_RETAIN  14                               (optional, days)
    MES_BACKUP_WEBHOOK  https://hooks.slack.com/...      (optional)
    MES_OFFSITE_DRY_RUN 1                                (optional, test only)

  Behaviour:
    1. Pick newest snapshot (ccl_mes.db.bak.*, excl. -shm/-wal) + newest *.tar.gz.
    2. robocopy them to the target.
    3. Verify the snapshot by SHA256 match on the destination.
    4. Optionally prune target files older than RETAIN days.
    5. Webhook alert on failure (best-effort).

  Task Scheduler example (nightly 02:30):
    schtasks /Create /TN "CCL-MES Off-site Backup" /SC DAILY /ST 02:30 ^
      /TR "powershell -NoProfile -ExecutionPolicy Bypass -File C:\ccl-mes\scripts\backup-offsite.ps1"
#>
[CmdletBinding()]
param(
  [string]$DataDir = $env:MES_DATA_DIR,
  [string]$Target  = $env:MES_OFFSITE_TARGET,
  [int]$RetainDays = $(if ($env:MES_OFFSITE_RETAIN) { [int]$env:MES_OFFSITE_RETAIN } else { 14 }),
  [string]$Webhook = $env:MES_BACKUP_WEBHOOK,
  [switch]$DryRun  = $($env:MES_OFFSITE_DRY_RUN -eq '1')
)

$ErrorActionPreference = 'Stop'
$hostShort = $env:COMPUTERNAME
$startTs   = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
function Log($m) { Write-Host "[offsite $hostShort $startTs] $m" }

function Notify($status, $msg) {
  if ([string]::IsNullOrWhiteSpace($Webhook)) { return }
  try {
    $payload = @{ text = "$hostShort — CCL-MES off-site backup $status`n$msg" } | ConvertTo-Json -Compress
    Invoke-RestMethod -Uri $Webhook -Method Post -ContentType 'application/json' -Body $payload -TimeoutSec 5 | Out-Null
  } catch { }
}

try {
  if ([string]::IsNullOrWhiteSpace($DataDir) -or [string]::IsNullOrWhiteSpace($Target)) {
    Write-Error "MES_DATA_DIR + MES_OFFSITE_TARGET must be set (or pass -DataDir / -Target)."
    exit 2
  }

  $backupDir = Join-Path $DataDir 'Backup'
  if (-not (Test-Path $backupDir)) {
    Write-Error "Backup dir not found: $backupDir. Enable the in-process scheduler first (Settings -> Backup)."
    exit 2
  }

  $sqliteDir = Join-Path $backupDir 'SQLite'
  $blobsDir  = Join-Path $backupDir 'Blobs'

  $latestSqlite = $null
  if (Test-Path $sqliteDir) {
    $latestSqlite = Get-ChildItem -Path $sqliteDir -File -Filter 'ccl_mes.db.bak.*' |
      Where-Object { $_.Name -notlike '*-shm' -and $_.Name -notlike '*-wal' } |
      Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
  }
  $latestBlobs = $null
  if (Test-Path $blobsDir) {
    $latestBlobs = Get-ChildItem -Path $blobsDir -File -Filter '*.tar.gz' |
      Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
  }

  if (-not $latestSqlite -and -not $latestBlobs) {
    Log "ERROR: no local backup artifacts found in $backupDir"
    Notify 'FAILED' "no local backups under $backupDir - scheduler may not be running"
    exit 1
  }

  Log ("latest sqlite: " + $(if ($latestSqlite) { $latestSqlite.Name } else { '<none>' }))
  Log ("latest blobs:  " + $(if ($latestBlobs)  { $latestBlobs.Name }  else { '<none>' }))
  Log "destination:   $Target"

  if (-not (Test-Path $Target)) {
    if ($DryRun) {
      Log "DRY-RUN: target $Target does not exist (would be created on real run)."
    } else {
      New-Item -ItemType Directory -Path $Target -Force | Out-Null
    }
  }

  $transferred = @()
  function Copy-One($file) {
    if ($DryRun) { Log "DRY-RUN: would copy $($file.Name)"; $script:transferred += $file.Name; return }
    # robocopy: /R:2 /W:5 retries, /Z restartable, /NP no per-% spam.
    & robocopy $file.DirectoryName $Target $file.Name /R:2 /W:5 /Z /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed (exit $LASTEXITCODE) for $($file.Name)" }
    $script:transferred += $file.Name
  }

  if ($latestSqlite) { Log "transferring sqlite snapshot..."; Copy-One $latestSqlite }
  if ($latestBlobs)  { Log "transferring blob tarball...";   Copy-One $latestBlobs }

  # Verify SQLite snapshot by SHA256 on the destination.
  if ($latestSqlite -and -not $DryRun) {
    $localSum  = (Get-FileHash $latestSqlite.FullName -Algorithm SHA256).Hash
    $remoteFile = Join-Path $Target $latestSqlite.Name
    $remoteSum = if (Test-Path $remoteFile) { (Get-FileHash $remoteFile -Algorithm SHA256).Hash } else { $null }
    if ($remoteSum -and $localSum -eq $remoteSum) {
      Log "checksum verified: $localSum"
    } else {
      $remoteShown = if ($remoteSum) { $remoteSum } else { '<unreadable>' }
      Log "WARN: checksum mismatch or remote unreadable. local=$localSum remote=$remoteShown"
      Notify 'WARN' "off-site sqlite checksum mismatch ($($latestSqlite.Name))"
    }
  }

  # Prune target files older than RetainDays.
  if ($RetainDays -gt 0 -and -not $DryRun) {
    $cutoff = (Get-Date).AddDays(-$RetainDays)
    $pruned = Get-ChildItem -Path $Target -File |
      Where-Object { ($_.Name -like 'ccl_mes.db.bak.*' -or $_.Name -like '*.tar.gz') -and $_.LastWriteTime -lt $cutoff }
    foreach ($f in $pruned) { Remove-Item $f.FullName -Force -ErrorAction SilentlyContinue }
    if ($pruned.Count -gt 0) { Log "pruned $($pruned.Count) target file(s) older than ${RetainDays}d" }
  }

  $msg = "transferred $($transferred.Count) file(s): $($transferred -join ', ')"
  Log "OK - $msg"
  Notify 'OK' $msg
  exit 0
}
catch {
  Log "ERROR: $($_.Exception.Message)"
  Notify 'FAILED' $_.Exception.Message
  exit 1
}
