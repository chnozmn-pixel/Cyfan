#Requires -Version 5.1
<#
    Cyfan derleme betigi.
    Windows ile birlikte gelen C# derleyicisini (csc.exe) kullanir; ayrica bir SDK
    kurmaniz gerekmez. Simge dosyasi yoksa kendisi uretir.

    Kullanim:  .\build.ps1            (derler)
               .\build.ps1 -Test      (derler ve kendi kendini test eder)
#>
param(
    [switch]$Test,
    [switch]$KeepIcon
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSCommandPath
$src = Join-Path $root 'src'
$ico = Join-Path $root 'app.ico'
$exe = Join-Path $root 'Cyfan.exe'

# ------------------------------------------------------------------ derleyici

$csc = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $csc) { throw "C# derleyicisi (csc.exe) bulunamadi. .NET Framework 4.x gerekli." }
Write-Host "Derleyici: $csc"

# ------------------------------------------------------------------ simge uret

function New-AppIcon([string]$path) {
    Add-Type -AssemblyName System.Drawing
    $sizes = @(256, 64, 48, 32, 16)
    $streams = @()

    foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap($s, $s)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'
        $g.Clear([System.Drawing.Color]::Transparent)

        $u = $s / 32.0   # 32 piksellik tasarimi olcekle

        # koyu yuvarlak kare zemin
        $r = [int](6 * $u)
        $rect = New-Object System.Drawing.Rectangle(0, 0, ($s - 1), ($s - 1))
        $path2 = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = $r * 2
        $path2.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
        $path2.AddArc(($rect.Right - $d), $rect.Y, $d, $d, 270, 90)
        $path2.AddArc(($rect.Right - $d), ($rect.Bottom - $d), $d, $d, 0, 90)
        $path2.AddArc($rect.X, ($rect.Bottom - $d), $d, $d, 90, 90)
        $path2.CloseFigure()
        $bg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 30, 38, 48))
        $g.FillPath($bg, $path2)

        # solda iki parca -> sagda tek butun  (birlestirme fikri)
        $accent = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 86, 196, 214))
        $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 245, 248, 250))

        $bw = [int](9 * $u)          # kucuk parca genisligi
        $bh = [int](7 * $u)          # kucuk parca yuksekligi
        $x1 = [int](5 * $u)
        $g.FillRectangle($accent, $x1, [int](6 * $u), $bw, $bh)
        $g.FillRectangle($accent, $x1, [int](19 * $u), $bw, $bh)

        $x2 = [int](18 * $u)
        $g.FillRectangle($white, $x2, [int](6 * $u), [int](9 * $u), [int](20 * $u))

        # birlestiren ince baglantilar
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 86, 196, 214), [single](1.6 * $u))
        $midY = [int](16 * $u)
        $g.DrawLine($pen, ($x1 + $bw), [int](9.5 * $u), $x2, $midY)
        $g.DrawLine($pen, ($x1 + $bw), [int](22.5 * $u), $x2, $midY)

        $pen.Dispose(); $g.Dispose()

        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $streams += , @{ Size = $s; Bytes = $ms.ToArray() }
        $ms.Dispose()
    }

    # ICO kabini elle yaz (PNG icerikli, Vista ve sonrasi destekler)
    $fs = [System.IO.File]::Create($path)
    $bw2 = New-Object System.IO.BinaryWriter($fs)
    try {
        $bw2.Write([uint16]0)                     # reserved
        $bw2.Write([uint16]1)                     # type = icon
        $bw2.Write([uint16]$streams.Count)
        $offset = 6 + 16 * $streams.Count
        foreach ($e in $streams) {
            $dim = if ($e.Size -ge 256) { 0 } else { $e.Size }
            $bw2.Write([byte]$dim)                # genislik
            $bw2.Write([byte]$dim)                # yukseklik
            $bw2.Write([byte]0)                   # renk sayisi
            $bw2.Write([byte]0)                   # reserved
            $bw2.Write([uint16]1)                 # planes
            $bw2.Write([uint16]32)                # bpp
            $bw2.Write([uint32]$e.Bytes.Length)
            $bw2.Write([uint32]$offset)
            $offset += $e.Bytes.Length
        }
        foreach ($e in $streams) { $bw2.Write($e.Bytes) }
    } finally { $bw2.Close(); $fs.Close() }
}

if (-not (Test-Path $ico) -or -not $KeepIcon) {
    Write-Host "Simge uretiliyor: app.ico"
    New-AppIcon $ico
}

# ------------------------------------------------------------------ derle

$files = Get-ChildItem (Join-Path $src '*.cs') | ForEach-Object { $_.FullName }
if ($files.Count -eq 0) { throw "src klasorunde .cs dosyasi yok." }
Write-Host ("Kaynak dosya sayisi: " + $files.Count)

$refs = @('System.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
          'System.IO.Compression.FileSystem.dll', 'System.Core.dll') -join ','

$cscArgs = @(
    '/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', '/langversion:5',
    "/out:$exe", "/win32icon:$ico", "/reference:$refs"
) + $files

if (Test-Path $exe) { Remove-Item $exe -Force }
$output = & $csc @cscArgs 2>&1
$code = $LASTEXITCODE

$errors = $output | Where-Object { $_ -match ': error ' }
$warnings = $output | Where-Object { $_ -match ': warning ' }

if ($warnings) {
    Write-Host "`nUyarilar:" -ForegroundColor DarkYellow
    $warnings | Select-Object -First 15 | ForEach-Object { Write-Host "  $_" }
}
if ($code -ne 0 -or $errors) {
    Write-Host "`nDERLEME BASARISIZ" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    if (-not $errors) { $output | ForEach-Object { Write-Host "  $_" } }
    exit 1
}

$info = Get-Item $exe
Write-Host ("`nDERLENDI: " + $info.FullName) -ForegroundColor Green
Write-Host ("Boyut: " + [math]::Round($info.Length / 1KB, 1) + " KB")

if ($Test) {
    Write-Host "`n--- Kendi kendini test ---"
    & $exe --selftest "$env:TEMP\cyfan_selftest" | Out-Host
    $tc = $LASTEXITCODE
    if ($tc -ne 0) { Write-Host "TESTLER BASARISIZ (kod $tc)" -ForegroundColor Red; exit $tc }
    Write-Host "Testler gecti." -ForegroundColor Green
}
