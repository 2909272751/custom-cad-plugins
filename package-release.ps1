param(
  [string]$Version = "0.1.9"
)

$ErrorActionPreference = "Stop"

$packages = @(
  @{ Name = "OpeningOutlinePlugin"; Command = "DKTRACE"; Dll = "OpeningOutlinePlugin\dist\OpeningOutlinePlugin-v0.1.1-autocad2022.dll" },
  @{ Name = "OuterOutlinePlugin"; Command = "PCOUTLINE"; Dll = "OuterOutlinePlugin\dist\OuterOutlinePlugin-v0.1.5-autocad2022.dll" },
  @{ Name = "HatchOuterPolylinePlugin"; Command = "HATCHPL"; Dll = "HatchOuterPolylinePlugin\dist\HatchOuterPolylinePlugin-v0.1.11-autocad2022.dll" },
  @{ Name = "LayerOffsetPlugin"; Command = "LOFFSET"; Dll = "LayerOffsetPlugin\dist\LayerOffsetPlugin-v0.1.1-autocad2021.dll" },
  @{ Name = "TextBoxSelectPlugin"; Command = "TXTBOXSEL"; Dll = "TextBoxSelectPlugin\dist\TextBoxSelectPlugin-v0.1.5-autocad2021.dll" },
  @{ Name = "NumberTextHighlightPlugin"; Command = "NUMRED"; Dll = "NumberTextHighlightPlugin\dist\NumberTextHighlightPlugin-v0.1.0-autocad2021.dll" },
  @{ Name = "numreplace"; Command = "NUMREPLACE"; Dll = "numreplace\dist\numreplace-v0.1.1-autocad2021.dll" },
  @{ Name = "beamcolor"; Command = "BEAMCOLOR"; Dll = "beamcolor\dist\beamcolor-v0.1.8-autocad2021.dll" }
)

Add-Type -AssemblyName System.IO.Compression.FileSystem

foreach ($package in $packages) {
  $dllPath = Join-Path $PSScriptRoot $package.Dll
  if (-not (Test-Path -LiteralPath $dllPath)) {
    throw "Missing DLL: $dllPath"
  }

  $dllName = Split-Path -Leaf $dllPath
  $distDir = Split-Path -Parent $dllPath
  $zipName = [System.IO.Path]::GetFileNameWithoutExtension($dllName) + ".zip"
  $zipPath = Join-Path $distDir $zipName
  $tempDir = Join-Path $distDir (".package-" + [System.IO.Path]::GetFileNameWithoutExtension($dllName))

  if (Test-Path -LiteralPath $tempDir) {
    Remove-Item -LiteralPath $tempDir -Recurse -Force
  }
  if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
  }

  New-Item -ItemType Directory -Path $tempDir | Out-Null
  Copy-Item -LiteralPath $dllPath -Destination (Join-Path $tempDir $dllName)

  $usage = @(
    "Plugin: $($package.Name)",
    "Command: $($package.Command)",
    "DLL: $dllName",
    "",
    "Steps:",
    "1. Extract this ZIP.",
    "2. Run unblock.ps1 in this folder:",
    "   powershell -ExecutionPolicy Bypass -File .\unblock.ps1",
    "3. Open AutoCAD and run NETLOAD.",
    "4. Select $dllName from this extracted folder.",
    "5. Run command $($package.Command).",
    "",
    "If AutoCAD still cannot load the DLL:",
    "- Restart AutoCAD and NETLOAD again.",
    "- Make sure the DLL file properties no longer show an Unblock button.",
    "- Do not load the DLL from inside the ZIP, browser cache, OneDrive temp folder, or another temporary location."
  )
  Set-Content -LiteralPath (Join-Path $tempDir "README.txt") -Value $usage -Encoding UTF8

  $unblock = @(
    '$ErrorActionPreference = "Stop"',
    '$dir = Split-Path -Parent $MyInvocation.MyCommand.Path',
    ('$dll = Join-Path $dir "' + $dllName + '"'),
    'if (-not (Test-Path -LiteralPath $dll)) {',
    '  throw "DLL not found: $dll"',
    '}',
    'Unblock-File -LiteralPath $dll',
    'Write-Host "Unblocked: $dll"',
    'Write-Host "You can now load this DLL in AutoCAD with NETLOAD."'
  )
  Set-Content -LiteralPath (Join-Path $tempDir "unblock.ps1") -Value $unblock -Encoding UTF8

  [System.IO.Compression.ZipFile]::CreateFromDirectory($tempDir, $zipPath)
  Remove-Item -LiteralPath $tempDir -Recurse -Force
  Write-Host "Packaged: $zipPath"
}

Write-Host "Package version: v$Version"
