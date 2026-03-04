# Generates a multi-resolution app.ico with a dark background and equalizer-bar design
# matching the app theme: BgMain=#16161F, Accent=#5865F2
# Output: WinUIAudioMixer\app.ico
Add-Type -AssemblyName System.Drawing

function New-IcoBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

    $bg     = [System.Drawing.Color]::FromArgb(22, 22, 31)
    $accent = [System.Drawing.Color]::FromArgb(88, 101, 242)
    $bgBrush  = New-Object System.Drawing.SolidBrush $bg
    $barBrush = New-Object System.Drawing.SolidBrush $accent

    $g.Clear([System.Drawing.Color]::Transparent)

    # ── Rounded background ────────────────────────────────────────────────────
    if ($size -ge 32) {
        $r    = [int]($size * 0.18)
        $d    = $r * 2
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc(0,            0,            $d, $d, 180, 90)
        $path.AddArc($size - $d,   0,            $d, $d, 270, 90)
        $path.AddArc($size - $d,   $size - $d,   $d, $d,   0, 90)
        $path.AddArc(0,            $size - $d,   $d, $d,  90, 90)
        $path.CloseFigure()
        $g.FillPath($bgBrush, $path)
    } else {
        $g.FillRectangle($bgBrush, 0, 0, $size, $size)
    }

    # ── Three equalizer bars ──────────────────────────────────────────────────
    $pad   = [Math]::Max(2, [int]($size * 0.14))
    $avail = $size - $pad * 2
    # barW * 3 + gap * 2 = avail  where gap = barW * 0.4
    $barW  = [int]($avail / 3.8)
    $gap   = [int](($avail - $barW * 3) / 2)
    $maxH  = $size - $pad * 2
    $heights = @(0.62, 1.0, 0.78)

    for ($i = 0; $i -lt 3; $i++) {
        $barH = [Math]::Max(2, [int]($maxH * $heights[$i]))
        $x    = $pad + $i * ($barW + $gap)
        $y    = $size - $pad - $barH

        if ($size -ge 48) {
            # Rounded top cap
            $capR = [int]($barW * 0.45)
            $cap  = $capR * 2
            $path = New-Object System.Drawing.Drawing2D.GraphicsPath
            $path.AddArc($x, $y, $cap, $cap, 180, 180)
            $path.AddLine($x + $barW, $y + $capR, $x + $barW, $y + $barH)
            $path.AddLine($x + $barW, $y + $barH, $x, $y + $barH)
            $path.AddLine($x, $y + $barH, $x, $y + $capR)
            $path.CloseFigure()
            $g.FillPath($barBrush, $path)
        } else {
            $g.FillRectangle($barBrush, $x, $y, $barW, $barH)
        }
    }

    $g.Dispose(); $bgBrush.Dispose(); $barBrush.Dispose()
    return $bmp
}

function Save-Ico([string]$OutputPath, [int[]]$Sizes) {
    $pngStreams = @()
    foreach ($sz in $Sizes) {
        $bmp = New-IcoBitmap $sz
        $ms  = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngStreams += $ms
        $bmp.Dispose()
    }

    $stream = [System.IO.File]::Create($OutputPath)
    $w      = New-Object System.IO.BinaryWriter $stream

    # Header
    $w.Write([uint16]0)                  # Reserved
    $w.Write([uint16]1)                  # Type = ICO
    $w.Write([uint16]$Sizes.Count)       # Image count

    # Directory: offset = 6-byte header + 16-byte entries × count
    $dataOffset = 6 + 16 * $Sizes.Count
    $runningOffset = $dataOffset
    for ($i = 0; $i -lt $Sizes.Count; $i++) {
        $sz = $Sizes[$i]
        $w.Write([byte]$(if ($sz -eq 256) { 0 } else { $sz }))   # Width  (0 → 256)
        $w.Write([byte]$(if ($sz -eq 256) { 0 } else { $sz }))   # Height (0 → 256)
        $w.Write([byte]0)                # ColorCount
        $w.Write([byte]0)                # Reserved
        $w.Write([uint16]1)              # Planes
        $w.Write([uint16]32)             # BitCount
        $w.Write([uint32]$pngStreams[$i].Length)
        $w.Write([uint32]$runningOffset)
        $runningOffset += $pngStreams[$i].Length
    }

    foreach ($ms in $pngStreams) { $w.Write($ms.ToArray()); $ms.Dispose() }
    $w.Close(); $stream.Close()
}

$out = Join-Path $PSScriptRoot "WinUIAudioMixer\app.ico"
Save-Ico -OutputPath $out -Sizes @(256, 64, 48, 32, 16)
Write-Host "Icon created: $out"
