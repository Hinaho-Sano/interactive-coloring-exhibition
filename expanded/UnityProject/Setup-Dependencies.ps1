param([string]$KinectSdk = "$env:ProgramFiles\Azure Kinect SDK v1.4.2")
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$managed = Join-Path $root 'Assets/Plugins/Managed'
$native = Join-Path $root 'Assets/Plugins/x86_64'
$cache = Join-Path $root 'Dependencies/download-cache'
New-Item -ItemType Directory -Force $managed,$native,$cache,(Join-Path $root 'Assets/StreamingAssets') | Out-Null
function Download($url,$target) {
    if (!(Test-Path -LiteralPath $target)) { Invoke-WebRequest -Uri $url -OutFile $target -UseBasicParsing }
}
function CheckHash($target,$expected) {
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $expected) { throw "Hash mismatch: $target. Do not use this download." }
}
$sensor = Join-Path $KinectSdk 'sdk/netstandard2.0/release/Microsoft.Azure.Kinect.Sensor.dll'
if (!(Test-Path -LiteralPath $sensor)) { throw 'Install Azure Kinect Sensor SDK 1.4.2 first, or specify -KinectSdk with its installed directory. See README.md.' }
Copy-Item -LiteralPath $sensor -Destination $managed
foreach ($name in @('k4a.dll','depthengine_2_0.dll')) {
    Copy-Item -LiteralPath (Join-Path $KinectSdk "sdk/windows-desktop/amd64/release/bin/$name") -Destination $native
}
$package = Join-Path $root 'Dependencies/com.github.homuler.mediapipe-0.16.3.tgz'
if (!(Test-Path -LiteralPath $package)) {
    $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/homuler/MediaPipeUnityPlugin/releases/tags/v0.16.3'
    $asset = @($release.assets | Where-Object name -eq 'com.github.homuler.mediapipe-0.16.3.tgz')
    if ($asset.Count -ne 1) { throw 'MediaPipe package not found in the official release.' }
    Download $asset[0].browser_download_url $package
}
CheckHash $package 'CC3E77A219E0B99618AE3BE64C31A566197DEEDC69C1E136ACF52D65D7CF2E79'
$model = Join-Path $root 'Assets/StreamingAssets/hand_landmarker.task'
Download 'https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task' $model
CheckHash $model 'FBC2A30080C3C557093B5DDFC334698132EB341044CCEE322CCF8BCF3607CDE1'
$packages = @(
    @('system.buffers','4.5.1','System.Buffers.dll'),
    @('system.memory','4.5.3','System.Memory.dll'),
    @('system.runtime.compilerservices.unsafe','4.5.2','System.Runtime.CompilerServices.Unsafe.dll'),
    @('system.reflection.emit.lightweight','4.7.0','System.Reflection.Emit.Lightweight.dll')
)
foreach ($p in $packages) {
    $id=$p[0]; $version=$p[1]; $dll=$p[2]
    $archive=Join-Path $cache "$id.$version.zip"
    $folder=Join-Path $cache "$id.$version"
    Download "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg" $archive
    if (!(Test-Path -LiteralPath $folder)) { Expand-Archive -LiteralPath $archive -DestinationPath $folder }
    $source=Join-Path $folder "lib/netstandard2.0/$dll"
    if (!(Test-Path -LiteralPath $source)) { throw "Expected library missing: $source" }
    Copy-Item -LiteralPath $source -Destination $managed
}
Write-Host 'Dependencies ready. Open this folder in Unity Hub with Unity 6000.6.4f1.'
