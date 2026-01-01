$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

function Invoke-Step {
  param(
    [Parameter(Mandatory=$true)][string]$Name,
    [Parameter(Mandatory=$true)][scriptblock]$Action
  )

  Write-Host "==> $Name"
  & $Action
  if ($LASTEXITCODE -ne $null -and $LASTEXITCODE -ne 0) {
    throw "Step failed ($Name) with exit code $LASTEXITCODE"
  }
}

Set-Location -LiteralPath $repoRoot

Invoke-Step -Name 'Publish Retro (WinForms) single-file' -Action {
  powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path -Path $repoRoot -ChildPath 'scripts\publish-win-x64-singlefile.ps1')
}

Invoke-Step -Name 'Publish Modern (WinUI 3) self-contained single-file' -Action {
  dotnet publish (Join-Path -Path $repoRoot -ChildPath 'Win98Get.Modern\Win98Get.Modern.csproj') -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true
}

Invoke-Step -Name 'Package dist/ and prune bin/obj' -Action {
  powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path -Path $repoRoot -ChildPath 'scripts\prune-unneeded-files.ps1')
}

Write-Host "All done. EXEs are in: $(Join-Path -Path $repoRoot -ChildPath 'dist')"