# Builds the Microsoft Store bundle (x64 and Arm64) into out/. Windows only: needs the Windows SDK
# for makeappx and makepri. The bundle is unsigned; the Store signs it when it's published.
$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot/../.."
$out = Join-Path $root "out"
$version = ([xml](Get-Content "$root/windows/Directory.Build.props")).Project.PropertyGroup.Version
$packageVersion = "$version.0"
$sdk = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.*\x64" | Sort-Object Name | Select-Object -Last 1
$makeappx = Join-Path $sdk.FullName "makeappx.exe"
$makepri = Join-Path $sdk.FullName "makepri.exe"

Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory "$out/msix" | Out-Null

foreach ($arch in "x64", "arm64") {
    $app = "$out/$arch"
    dotnet publish "$root/windows/src/BringLater" -c Release -r "win-$arch" -p:PublishSingleFile=false -p:Version=$version -o $app
    if ($LASTEXITCODE) { throw "dotnet publish failed for $arch" }

    New-Item -ItemType Directory "$app/Assets" | Out-Null
    Copy-Item "$root/assets/brand/generated/store/*" "$app/Assets"
    (Get-Content "$PSScriptRoot/Package.appxmanifest" -Raw).Replace('$VERSION$', $packageVersion).Replace('$ARCH$', $arch) |
        Set-Content "$app/AppxManifest.xml" -Encoding utf8

    & $makepri createconfig /cf "$out/priconfig.xml" /dq en-US /o
    & $makepri new /pr $app /cf "$out/priconfig.xml" /mn "$app/AppxManifest.xml" /of "$app/resources.pri" /o
    if ($LASTEXITCODE) { throw "makepri failed for $arch" }

    & $makeappx pack /d $app /p "$out/msix/BringLater_${packageVersion}_$arch.msix" /o
    if ($LASTEXITCODE) { throw "makeappx pack failed for $arch" }
}

& $makeappx bundle /d "$out/msix" /p "$out/BringLater_$packageVersion.msixbundle" /bv $packageVersion /o
if ($LASTEXITCODE) { throw "makeappx bundle failed" }
