param(
    [string]$Version = "1.0.7"
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Dotnet = Join-Path $Root ".tools\dotnet\dotnet.exe"
if (-not (Test-Path $Dotnet)) {
    $DotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($DotnetCommand) { $Dotnet = $DotnetCommand.Source }
}
if (-not (Test-Path $Dotnet)) {
    throw "Не найден .NET SDK 10. Установите SDK для сборки приложения и повторите запуск."
}

$ToolsDir = Join-Path $Root ".tools\ffmpeg"
$Archive = Join-Path $Root ".tools\ffmpeg-lgpl-shared.zip"
$ExtractDir = Join-Path $Root ".tools\ffmpeg-extracted"
$PublishDir = Join-Path $Root "publish"
$ReleaseDir = Join-Path $Root "release"
$FfmpegUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n9.0-latest-win64-lgpl-shared-9.0.zip"

New-Item -ItemType Directory -Force (Join-Path $Root ".tools") | Out-Null
if (-not (Test-Path $Archive)) {
    Write-Host "Загружаю LGPL-сборку FFmpeg..."
    Invoke-WebRequest -Uri $FfmpegUrl -OutFile $Archive
}

if (-not (Test-Path (Join-Path $ToolsDir "ffmpeg.exe"))) {
    if (Test-Path $ExtractDir) { Remove-Item -LiteralPath $ExtractDir -Recurse -Force }
    Expand-Archive -LiteralPath $Archive -DestinationPath $ExtractDir -Force
    $FfmpegBin = Get-ChildItem -LiteralPath $ExtractDir -Filter "ffmpeg.exe" -Recurse | Select-Object -First 1
    if (-not $FfmpegBin) { throw "В архиве FFmpeg не найден ffmpeg.exe." }
    New-Item -ItemType Directory -Force $ToolsDir | Out-Null
    Copy-Item -Path (Join-Path $FfmpegBin.Directory.FullName "*") -Destination $ToolsDir -Recurse -Force
}

$LicenseFiles = Get-ChildItem -LiteralPath $ExtractDir -File -Recurse |
    Where-Object { $_.Name -match "^(COPYING|LICENSE|NOTICE)(\.|$)" }
if ($LicenseFiles) {
    $LicenseDir = Join-Path $ToolsDir "licenses"
    New-Item -ItemType Directory -Force $LicenseDir | Out-Null
    Copy-Item -LiteralPath $LicenseFiles.FullName -Destination $LicenseDir -Force
}

if (Test-Path $PublishDir) { Remove-Item -LiteralPath $PublishDir -Recurse -Force }
New-Item -ItemType Directory -Force $PublishDir | Out-Null
& $Dotnet publish (Join-Path $Root "ConverterPlus\ConverterPlus.csproj") `
    --configuration Release --runtime win-x64 --self-contained true `
    --output $PublishDir `
    -p:Version=$Version `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None
if ($LASTEXITCODE -ne 0) { throw "Публикация приложения завершилась с ошибкой." }

$BundledToolsDir = Join-Path $PublishDir "tools"
New-Item -ItemType Directory -Force $BundledToolsDir | Out-Null
Copy-Item -Path (Join-Path $ToolsDir "*") -Destination $BundledToolsDir -Recurse -Force
Copy-Item -LiteralPath (Join-Path $Root "THIRD-PARTY-NOTICES.txt") -Destination $PublishDir -Force

$Inno = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $Inno)) { throw "Не найден компилятор Inno Setup. Установите Inno Setup 6 и повторите запуск." }

New-Item -ItemType Directory -Force $ReleaseDir | Out-Null
& $Inno "/DAppVersion=$Version" "/DSourceDir=$PublishDir" "/DOutputDir=$ReleaseDir" (Join-Path $Root "installer.iss")
if ($LASTEXITCODE -ne 0) { throw "Создание установщика завершилось с ошибкой." }

Write-Host "Установщик создан: $(Join-Path $ReleaseDir "ConverterPlus-Setup-$Version.exe")"
