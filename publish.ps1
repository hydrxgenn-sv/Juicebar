# Publishes self-contained Juicebar 2 packages (no .NET needed on the target) for Windows, Linux and macOS into dist/.
# Usage:  pwsh publish.ps1 [-Targets win-x64,linux-x64,osx-arm64,osx-x64] [-Version 2.1.0]
# Works in Windows PowerShell 5.1 and in PowerShell 7 on Windows, Linux and macOS (GitHub runners).
# On a Windows host every package is a zip. On Linux/macOS hosts file permissions are kept:
# tar.gz for Linux, and for macOS the .app is signed ad hoc and zipped with ditto.
param(
    [string[]]$Targets = @('win-x64', 'linux-x64', 'osx-arm64', 'osx-x64'),
    [string]$Version = ''
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$onWindows = $env:OS -eq 'Windows_NT'
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet -and $env:LOCALAPPDATA) { $dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe' }
$proj = Join-Path $root 'app/Juicebar.App/Juicebar.App.csproj'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$extra = @()
if ($Version) { $extra += "-p:Version=$($Version.TrimStart('v', 'V'))" }

foreach ($rid in $Targets) {
    $out = Join-Path $root "dist/$rid"
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    & $dotnet publish $proj -c Release -r $rid --self-contained true -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out -nologo -v q @extra
    if ($LASTEXITCODE -ne 0) { throw "Publishing $rid failed" }

    if ($rid -like 'linux-*') {
        Copy-Item (Join-Path $root 'packaging/linux/*') $out
        Copy-Item (Join-Path $root 'packaging/icons/juicebar.png') $out
    }
    if ($rid -like 'osx-*') {
        # .app bundle: Contents/MacOS/Juicebar + Info.plist (menu bar only, no Dock icon)
        $app = Join-Path $out 'Juicebar.app'
        $macos = Join-Path $app 'Contents/MacOS'
        New-Item -ItemType Directory -Force $macos | Out-Null
        Get-ChildItem $out -File | Move-Item -Destination $macos
        Copy-Item (Join-Path $root 'packaging/macos/Info.plist') (Join-Path $app 'Contents/Info.plist')
        $res = Join-Path $app 'Contents/Resources'
        New-Item -ItemType Directory -Force $res | Out-Null
        Copy-Item (Join-Path $root 'packaging/icons/juicebar.icns') $res
        Copy-Item (Join-Path $root 'packaging/macos/README.txt') $out
        Copy-Item (Join-Path $root 'packaging/macos/LEEME.txt') $out
    }

    $base = Join-Path $root "dist/Juicebar-$rid"
    Remove-Item "$base.zip", "$base.tar.gz" -Force -ErrorAction SilentlyContinue
    if (-not $onWindows -and $rid -like 'linux-*') {
        & chmod +x (Join-Path $out 'Juicebar') (Join-Path $out 'install.sh')
        & tar -czf "$base.tar.gz" -C $out .
        Write-Host "Done: dist/Juicebar-$rid.tar.gz"
    }
    elseif (-not $onWindows -and $rid -like 'osx-*' -and $IsMacOS) {
        $app = Join-Path $out 'Juicebar.app'
        & chmod +x (Join-Path $app 'Contents/MacOS/Juicebar')
        & codesign --force --deep --sign - $app
        & ditto -c -k --keepParent $out "$base.zip"
        Write-Host "Done: dist/Juicebar-$rid.zip"
    }
    else {
        Compress-Archive -Path (Join-Path $out '*') -DestinationPath "$base.zip"
        Write-Host "Done: dist/Juicebar-$rid.zip"
    }
}
