$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

$retroExe = Join-Path -Path $repoRoot -ChildPath 'Win98Get\bin\Release\net10.0-windows\win-x64\publish\Win98Get.exe'
$modernExe = Join-Path -Path $repoRoot -ChildPath 'Win98Get.Modern\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\Win98Get.Modern.exe'

$distRoot = Join-Path -Path $repoRoot -ChildPath 'dist'
$distRetro = Join-Path -Path $distRoot -ChildPath 'retro'
$distModern = Join-Path -Path $distRoot -ChildPath 'modern'

New-Item -ItemType Directory -Force -Path $distRetro | Out-Null
New-Item -ItemType Directory -Force -Path $distModern | Out-Null

function Copy-ExeIfExists {
  param(
    [Parameter(Mandatory=$true)][string]$SourceExe,
    [Parameter(Mandatory=$true)][string]$DestDir
  )

  if (Test-Path -LiteralPath $SourceExe) {
    Copy-Item -LiteralPath $SourceExe -Destination $DestDir -Force
    $destExe = Join-Path -Path $DestDir -ChildPath (Split-Path -Leaf $SourceExe)
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $destExe).Hash
    Write-Host "Copied: $destExe"
    Write-Host "SHA256: $hash"
  } else {
    Write-Warning "EXE not found (skipping copy): $SourceExe"
  }
}

Write-Host "Packaging published EXEs into: $distRoot"
Copy-ExeIfExists -SourceExe $retroExe -DestDir $distRetro
Copy-ExeIfExists -SourceExe $modernExe -DestDir $distModern

# If the user launched the published apps, the EXEs may be locked.
Write-Host "Stopping running Win98Get processes (if any)..."
@('Win98Get', 'Win98Get.Modern') | ForEach-Object {
  Stop-Process -Name $_ -Force -ErrorAction SilentlyContinue
}

# Prune build artifacts
$artifactDirs = @(
  (Join-Path -Path $repoRoot -ChildPath 'Win98Get\bin'),
  (Join-Path -Path $repoRoot -ChildPath 'Win98Get\obj'),
  (Join-Path -Path $repoRoot -ChildPath 'Win98Get.Core\bin'),
  (Join-Path -Path $repoRoot -ChildPath 'Win98Get.Core\obj'),
  (Join-Path -Path $repoRoot -ChildPath 'Win98Get.Modern\bin'),
  (Join-Path -Path $repoRoot -ChildPath 'Win98Get.Modern\obj')
)

Write-Host "Pruning build artifact folders (bin/obj)..."
foreach ($dir in $artifactDirs) {
  if (Test-Path -LiteralPath $dir) {
    try {
      Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction Stop
      Write-Host "Deleted: $dir"
    } catch {
      Write-Warning "Failed to delete: $dir"
      Write-Warning $_.Exception.Message
    }
  }
}

Write-Host "Pruning *.csproj.user files..."
Get-ChildItem -LiteralPath $repoRoot -Recurse -File -Filter '*.csproj.user' -ErrorAction SilentlyContinue |
  ForEach-Object {
    Remove-Item -LiteralPath $_.FullName -Force
    Write-Host "Deleted: $($_.FullName)"
  }

Write-Host "Done. Distributables are in: $distRoot"