# dotnet 包装脚本：将 APPDATA/LOCALAPPDATA 重定向到工作区，规避沙箱对用户目录的写入限制。
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$DotnetArgs)
$redirect = Join-Path $PSScriptRoot "..\.local-redirect"
$env:APPDATA = Join-Path $redirect "AppData\Roaming"
$env:LOCALAPPDATA = Join-Path $redirect "AppData\Local"
& dotnet @DotnetArgs
exit $LASTEXITCODE
