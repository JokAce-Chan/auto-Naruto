# P7 发布脚本：发布主程序并把 scrcpy 组件拷入 vendor/scrcpy。
# 用法：
#   .\tools\publish.ps1              # 默认：框架依赖（本机需 .NET 8 运行时），离线可用
#   .\tools\publish.ps1 -SingleFile  # 框架依赖单文件（需联网下载 ILLink 包）
#   .\tools\publish.ps1 -SelfContained  # 自包含单文件（体积大，需联网下载运行时包）
param(
    [switch]$SingleFile,
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')

# 重定向用户目录，规避权限对 LocalAppData 的写入
$redirect = Join-Path $root '.local-redirect'
$env:APPDATA = Join-Path $redirect 'AppData\Roaming'
$env:LOCALAPPDATA = Join-Path $redirect 'AppData\Local'

$proj = Join-Path $root 'src\NaruttoTimer.App\NaruttoTimer.App.csproj'
$out = Join-Path $root 'publish'

$args = @('publish', $proj, '-c', 'Release', '-o', $out)
if ($SelfContained) {
    $args += @('-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true')
}
elseif ($SingleFile) {
    $args += @('-p:PublishSingleFile=true')
}
& dotnet @args
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败，退出码 $LASTEXITCODE" }

# 拷贝 scrcpy 组件到 vendor/scrcpy（发布目录随程序放置）
$srcScrcpy = Join-Path $root 'scrcpy-win64-v4.1\scrcpy-win64-v4.1'
if (-not (Test-Path $srcScrcpy)) { Write-Warning "未找到 $srcScrcpy，跳过 vendor 拷贝（应用运行时需 FFmpeg/adb）" }
else {
    $vendor = Join-Path $out 'vendor\scrcpy'
    New-Item -ItemType Directory -Force -Path $vendor | Out-Null
    $files = @('adb.exe','AdbWinApi.dll','AdbWinUsbApi.dll','scrcpy.exe','scrcpy-server','SDL3.dll',
        'avcodec-62.dll','avformat-62.dll','avutil-60.dll','swresample-6.dll','libusb-1.0.dll')
    foreach ($f in $files) {
        $src = Join-Path $srcScrcpy $f
        if (Test-Path $src) { Copy-Item -LiteralPath $src -Destination $vendor -Force }
    }
    Write-Output "已拷贝 scrcpy 组件到 $vendor"
}

Write-Output "发布完成：$out\NaruttoTimer.App.exe"