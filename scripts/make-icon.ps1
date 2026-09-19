$ErrorActionPreference = "Stop"

# 生成 GpuKeepAlive 应用图标 (src/GpuKeepAlive.Gui/Assets/app.ico)：
# 深色圆角底 + 青色心跳折线，256/48/32/16 四档尺寸，PNG 压缩嵌入 ICO。
# 用法: powershell -ExecutionPolicy Bypass -File scripts/make-icon.ps1

$root = Split-Path $PSScriptRoot -Parent
$outPath = Join-Path $root "src\GpuKeepAlive.Gui\Assets\app.ico"
$outDir = Split-Path $outPath -Parent
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

Add-Type -AssemblyName System.Drawing

function Render-Frame {
    param([int]$Size)

    $bmp = New-Object -TypeName System.Drawing.Bitmap -ArgumentList $Size, $Size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)

        # 深色圆角背景
        $corner = [Math]::Max(2, [int]($Size * 0.22))
        $edge = [float]1
        $side = [float]($Size - 2)
        $rect = New-Object -TypeName System.Drawing.RectangleF -ArgumentList $edge, $edge, $side, $side
        $path = New-Object -TypeName System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc($rect.X, $rect.Y, $corner, $corner, 180, 90)
        $path.AddArc($rect.Right - $corner, $rect.Y, $corner, $corner, 270, 90)
        $path.AddArc($rect.Right - $corner, $rect.Bottom - $corner, $corner, $corner, 0, 90)
        $path.AddArc($rect.X, $rect.Bottom - $corner, $corner, $corner, 90, 90)
        $path.CloseFigure()
        $bgTop = [System.Drawing.Color]::FromArgb(255, 30, 42, 60)
        $bgBottom = [System.Drawing.Color]::FromArgb(255, 12, 18, 28)
        $brush = New-Object -TypeName System.Drawing.Drawing2D.LinearGradientBrush -ArgumentList $rect, $bgTop, $bgBottom, ([single]90)
        $g.FillPath($brush, $path)
        $brush.Dispose()

        # 青色心跳折线 (ECG)
        $penWidth = [Math]::Max(1.5, $Size * 0.075)
        $pen = New-Object -TypeName System.Drawing.Pen -ArgumentList ([System.Drawing.Color]::FromArgb(255, 34, 211, 238)), ([single]$penWidth)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $anchors = @(
            @(0.14, 0.56), @(0.30, 0.56), @(0.38, 0.32), @(0.47, 0.76),
            @(0.55, 0.44), @(0.62, 0.56), @(0.86, 0.56)
        )
        $pts = New-Object -TypeName 'System.Drawing.PointF[]' -ArgumentList $anchors.Count
        for ($i = 0; $i -lt $anchors.Count; $i++) {
            $x = [single]($anchors[$i][0] * $Size)
            $y = [single]($anchors[$i][1] * $Size)
            $pts[$i] = New-Object -TypeName System.Drawing.PointF -ArgumentList $x, $y
        }
        $g.DrawLines($pen, $pts)
        $pen.Dispose()
        $path.Dispose()
    }
    finally {
        $g.Dispose()
    }

    $ms = New-Object -TypeName System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return ,$bytes
}

$sizes = @(256, 48, 32, 16)
$frames = @()
foreach ($s in $sizes) {
    $frameBytes = Render-Frame -Size $s
    $frames += ,($frameBytes)
}

$fs = [System.IO.File]::Create($outPath)
$bw = New-Object -TypeName System.IO.BinaryWriter -ArgumentList $fs
# ICONDIR
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = $sizes[$i]
    $data = $frames[$i]
    # ICONDIRENTRY (宽高各 1 字节，0 表示 256)
    $dimByte = [byte]$(if ($dim -ge 256) { 0 } else { $dim })
    $bw.Write($dimByte)
    $bw.Write($dimByte)
    $bw.Write([byte]0)       # 调色板数
    $bw.Write([byte]0)       # 保留
    $bw.Write([uint16]1)     # 色彩平面
    $bw.Write([uint16]32)    # 位深
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $frames) { $bw.Write($data) }
$bw.Flush()
$bw.Close()

Write-Host "[OK] Icon generated: $outPath ($((Get-Item $outPath).Length) bytes)"
