param(
  [string]$PfxBase64 = $env:WIN98GET_SIGN_PFX_B64,
  [SecureString]$PfxPassword,
  [string]$TimestampUrl = $env:WIN98GET_SIGN_TIMESTAMP_URL
)

$ErrorActionPreference = 'Stop'

if (-not $TimestampUrl) {
  # RFC3161 timestamp (widely used)
  $TimestampUrl = 'http://timestamp.digicert.com'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot

function Find-SignTool {
  $candidates = @(
    (Get-Command signtool.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    (Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
      Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName)
  ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique

  if (-not $candidates) {
    throw "signtool.exe not found. Install Windows SDK / Build Tools that include SignTool, or ensure signtool.exe is on PATH."
  }

  return $candidates[0]
}

if (-not $PfxBase64) {
  throw "Missing PFX certificate. Provide -PfxBase64 or set WIN98GET_SIGN_PFX_B64."
}

if (-not $PfxPassword) {
  if (-not $env:WIN98GET_SIGN_PFX_PASSWORD) {
    throw "Missing PFX password. Provide -PfxPassword (SecureString) or set WIN98GET_SIGN_PFX_PASSWORD."
  }

  $PfxPassword = ConvertTo-SecureString -String $env:WIN98GET_SIGN_PFX_PASSWORD -AsPlainText -Force
}

function Get-PlainTextFromSecureString {
  param([Parameter(Mandatory=$true)][SecureString]$Value)

  $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
  try {
    return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
  }
  finally {
    if ($bstr -ne [IntPtr]::Zero) {
      [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
  }
}

$signTool = Find-SignTool

$tempDir = Join-Path $env:TEMP ("Win98Get_Signing_" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null
$pfxPath = Join-Path $tempDir 'codesign.pfx'

try {
  [IO.File]::WriteAllBytes($pfxPath, [Convert]::FromBase64String($PfxBase64))

  $exePaths = @(
    Join-Path $repoRoot 'dist\retro\Win98Get.exe',
    Join-Path $repoRoot 'dist\modern\Win98Get.Modern.exe'
  ) | Where-Object { Test-Path -LiteralPath $_ }

  if (-not $exePaths) {
    throw "No EXEs found in dist/. Run publish packaging first (./scripts/publish-and-prune.ps1)."
  }

  foreach ($exe in $exePaths) {
    Write-Host "Signing: $exe"
    $plainPassword = Get-PlainTextFromSecureString -Value $PfxPassword
    & $signTool sign /fd SHA256 /f $pfxPath /p $plainPassword /tr $TimestampUrl /td SHA256 /v $exe
    if ($LASTEXITCODE -ne 0) {
      throw "signtool sign failed for $exe (exit $LASTEXITCODE)"
    }

    & $signTool verify /pa /v $exe
    if ($LASTEXITCODE -ne 0) {
      throw "signtool verify failed for $exe (exit $LASTEXITCODE)"
    }
  }

  Write-Host 'Signing complete.'
}
finally {
  if (Test-Path -LiteralPath $tempDir) {
    Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
  }
}
