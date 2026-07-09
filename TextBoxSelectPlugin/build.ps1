param(
  [string]$AcadPath = "D:\autocad\AutoCAD 2021",
  [string]$AcadLabel = "autocad2021"
)

$ErrorActionPreference = "Stop"

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$version = "0.1.2"
$outDir = Join-Path $PSScriptRoot "dist"
$out = Join-Path $outDir "TextBoxSelectPlugin-v$version-$AcadLabel.dll"

foreach ($dll in @("acmgd.dll", "acdbmgd.dll", "accoremgd.dll")) {
  $path = Join-Path $AcadPath $dll
  if (-not (Test-Path -LiteralPath $path)) {
    throw "Missing AutoCAD API DLL: $path"
  }
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

& $csc `
  /target:library `
  /platform:x64 `
  /out:$out `
  /reference:"$AcadPath\acmgd.dll" `
  /reference:"$AcadPath\acdbmgd.dll" `
  /reference:"$AcadPath\accoremgd.dll" `
  (Join-Path $PSScriptRoot "TextBoxSelectPlugin.cs")

Write-Host "Built: $out"
