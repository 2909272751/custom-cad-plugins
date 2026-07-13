param(
  [string]$AcadPath = "C:\Program Files\Autodesk\AutoCAD 2022",
  [string]$AcadLabel = "autocad2022"
)

$ErrorActionPreference = "Stop"

$plugins = @(
  "OpeningOutlinePlugin",
  "OuterOutlinePlugin",
  "HatchOuterPolylinePlugin",
  "LayerOffsetPlugin",
  "TextBoxSelectPlugin",
  "NumberTextHighlightPlugin"
)

foreach ($plugin in $plugins) {
  $script = Join-Path $PSScriptRoot (Join-Path $plugin "build.ps1")
  if (-not (Test-Path -LiteralPath $script)) {
    throw "Missing build script: $script"
  }

  Write-Host "Building $plugin..."
  & powershell -ExecutionPolicy Bypass -File $script -AcadPath $AcadPath -AcadLabel $AcadLabel
  if ($LASTEXITCODE -ne 0) {
    throw "Build failed: $plugin"
  }
}

Write-Host "All plugins built."
