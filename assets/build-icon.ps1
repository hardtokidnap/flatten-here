#!/usr/bin/env pwsh
# Generates a multi-resolution flatten.ico from PowerShell + System.Drawing.
# Re-run this any time you want to tweak the design; commit both the script and
# the resulting flatten.ico so the build stays reproducible.
#
# Design: three short vertical bars of varying height (representing a nested
# tree) being collapsed by a downward arrow onto a single horizontal floor
# line. Reads clearly down to 16x16.

[CmdletBinding()]
param(
    [string]$OutPath = (Join-Path (Split-Path -Parent $PSCommandPath) 'flatten.ico'),
    [int[]]$Sizes = @(16, 24, 32, 48, 64, 128, 256)
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Draw-Icon {
    param([System.Drawing.Graphics]$g, [int]$s)

    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $blue  = [System.Drawing.Color]::FromArgb(255, 56, 132, 255)
    $stroke = [Math]::Max(1, [int]([Math]::Round($s / 10.0)))

    $pen = New-Object System.Drawing.Pen($blue, [single]$stroke)
    $pen.StartCap = 'Round'
    $pen.EndCap   = 'Round'
    $pen.LineJoin = 'Round'

    # Three "stacked" vertical bars at the top, varying heights.
    $top    = [single]($s * 0.18)
    $barY   = [single]($s * 0.42)
    $xLeft  = [single]($s * 0.22)
    $xMid   = [single]($s * 0.50)
    $xRight = [single]($s * 0.78)
    $g.DrawLine($pen, $xLeft,  ([single]($s * 0.30)), $xLeft,  $barY)
    $g.DrawLine($pen, $xMid,   $top,                  $xMid,   $barY)
    $g.DrawLine($pen, $xRight, ([single]($s * 0.34)), $xRight, $barY)

    # Down arrow in the centre.
    $shaftTop    = [single]($s * 0.48)
    $shaftBottom = [single]($s * 0.74)
    $arrowSide   = [single]($s * 0.18)
    $g.DrawLine($pen, $xMid, $shaftTop, $xMid, $shaftBottom)
    $g.DrawLine($pen, ($xMid - $arrowSide), ($shaftBottom - $arrowSide * 0.9), $xMid, $shaftBottom)
    $g.DrawLine($pen, ($xMid + $arrowSide), ($shaftBottom - $arrowSide * 0.9), $xMid, $shaftBottom)

    # Floor: the "flat" base line.
    $floorY     = [single]($s * 0.88)
    $floorLeft  = [single]($s * 0.14)
    $floorRight = [single]($s * 0.86)
    $g.DrawLine($pen, $floorLeft, $floorY, $floorRight, $floorY)

    $pen.Dispose()
}

function Render-Png {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        Draw-Icon -g $g -s $Size
    }
    finally {
        $g.Dispose()
    }
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

# Render each requested size into a PNG byte array.
$frames = foreach ($size in $Sizes) {
    [pscustomobject]@{
        Size  = $size
        Bytes = Render-Png -Size $size
    }
}

# Write a multi-resolution ICO (PNG-encoded entries, supported on Vista+).
$dir = Split-Path -Parent $OutPath
if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

$fs = [System.IO.File]::Create($OutPath)
try {
    $bw = New-Object System.IO.BinaryWriter($fs)
    try {
        # ICONDIR
        $bw.Write([uint16]0)              # reserved
        $bw.Write([uint16]1)              # type = ICON
        $bw.Write([uint16]$frames.Count)  # image count

        # Compute offset to first image: header (6) + N * directory entry (16).
        $offset = 6 + ($frames.Count * 16)

        # ICONDIRENTRY * N
        foreach ($f in $frames) {
            $w = if ($f.Size -ge 256) { [byte]0 } else { [byte]$f.Size }  # 0 == 256
            $bw.Write([byte]$w)                # width
            $bw.Write([byte]$w)                # height
            $bw.Write([byte]0)                 # palette count (0 = no palette)
            $bw.Write([byte]0)                 # reserved
            $bw.Write([uint16]1)               # color planes
            $bw.Write([uint16]32)              # bits per pixel
            $bw.Write([uint32]$f.Bytes.Length) # bytes in image data
            $bw.Write([uint32]$offset)         # absolute offset
            $offset += $f.Bytes.Length
        }

        # PNG payloads
        foreach ($f in $frames) {
            $bw.Write($f.Bytes)
        }
    }
    finally {
        $bw.Dispose()
    }
}
finally {
    $fs.Dispose()
}

$sizeKB = [math]::Round((Get-Item $OutPath).Length / 1KB, 2)
Write-Host "Wrote $OutPath  ($sizeKB KB, $($frames.Count) frames: $($Sizes -join ', '))" -ForegroundColor Green
