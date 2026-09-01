# Generate assets/app.ico: 256x256 32bpp BGRA diamond icon (pure byte math).
$ErrorActionPreference = 'Stop'
$size = 256
$pixels = New-Object byte[] ($size * $size * 4)

function Set-Pixel($x, $y, $r, $g, $b, $a) {
    $idx = (($size - 1 - $y) * $size + $x) * 4
    $pixels[$idx] = $b
    $pixels[$idx + 1] = $g
    $pixels[$idx + 2] = $r
    $pixels[$idx + 3] = $a
}

$cx = 128
$cy = 128
for ($y = 0; $y -lt $size; $y++) {
    for ($x = 0; $x -lt $size; $x++) {
        $dx = [Math]::Abs($x - $cx)
        $dy = [Math]::Abs($y - $cy)
        $d = $dx + $dy
        if ($d -gt 96) { continue }
        $cornerDist = -1
        if ($dx -gt 30 -and $dy -gt 30) { $cornerDist = ($dx - 30) + ($dy - 30) }
        if ($cornerDist -gt 26) { continue }
        Set-Pixel $x $y 0x14 0x17 0x1D 255
        if ($d -ge 80) { Set-Pixel $x $y 0xFF 0xFF 0xFF 255 }
        elseif ($d -le 62) { Set-Pixel $x $y 0x4F 0x9D 0xF5 255 }
        else { Set-Pixel $x $y 0x2E 0x6F 0xD0 255 }
    }
}

$rowBytes = $size * 4
$andRowBytes = [Math]::Ceiling($size / 32.0) * 4
$dibSize = 40 + $rowBytes * $size + $andRowBytes * $size
$ico = New-Object byte[] (22 + $dibSize)

$ico[0] = 0; $ico[1] = 0
$ico[2] = 1; $ico[3] = 0
$ico[4] = 1; $ico[5] = 0
$ico[6] = 0; $ico[7] = 0; $ico[8] = 0; $ico[9] = 0
$ico[10] = 1; $ico[11] = 0
$ico[12] = 32; $ico[13] = 0
$ico[14] = $dibSize -band 0xFF
$ico[15] = ($dibSize -shr 8) -band 0xFF
$ico[16] = ($dibSize -shr 16) -band 0xFF
$ico[17] = ($dibSize -shr 24) -band 0xFF
$ico[18] = 22; $ico[19] = 0; $ico[20] = 0; $ico[21] = 0

$ico[22] = 40; $ico[23] = 0; $ico[24] = 0; $ico[25] = 0
$ico[26] = $size -band 0xFF
$ico[27] = ($size -shr 8) -band 0xFF
$ico[28] = 0; $ico[29] = 0
$ico[30] = ($size * 2) -band 0xFF
$ico[31] = (($size * 2) -shr 8) -band 0xFF
$ico[32] = 0; $ico[33] = 0
$ico[34] = 1; $ico[35] = 0
$ico[36] = 32; $ico[37] = 0
$ico[38] = 0; $ico[39] = 0; $ico[40] = 0; $ico[41] = 0
$imgSize = $rowBytes * $size + $andRowBytes * $size
$ico[42] = $imgSize -band 0xFF
$ico[43] = ($imgSize -shr 8) -band 0xFF
$ico[44] = ($imgSize -shr 16) -band 0xFF
$ico[45] = ($imgSize -shr 24) -band 0xFF

[Array]::Copy($pixels, 0, $ico, 22 + 40, $pixels.Length)

$outDir = Join-Path $PSScriptRoot '..\assets'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$out = Join-Path $outDir 'app.ico'
[System.IO.File]::WriteAllBytes($out, $ico)
Write-Output "Generated $out ($($ico.Length) bytes)"