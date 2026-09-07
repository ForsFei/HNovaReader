param(
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'HNovaReader\Assets')
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

function New-RoundedRectanglePath {
    param(
        [System.Drawing.RectangleF]$Bounds,
        [single]$Radius
    )

    $diameter = $Radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($Bounds.Left, $Bounds.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Bounds.Left, $Bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-BookPagePath {
    param(
        [single]$Size,
        [bool]$RightPage
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $centerTop = [System.Drawing.PointF]::new($Size * 0.50, $Size * 0.31)
    $centerBottom = [System.Drawing.PointF]::new($Size * 0.50, $Size * 0.75)

    if ($RightPage) {
        $outerTop = [System.Drawing.PointF]::new($Size * 0.79, $Size * 0.26)
        $outerBottom = [System.Drawing.PointF]::new($Size * 0.80, $Size * 0.65)
        $path.AddBezier(
            $centerTop,
            [System.Drawing.PointF]::new($Size * 0.59, $Size * 0.27),
            [System.Drawing.PointF]::new($Size * 0.70, $Size * 0.25),
            $outerTop)
        $path.AddBezier(
            $outerTop,
            [System.Drawing.PointF]::new($Size * 0.81, $Size * 0.38),
            [System.Drawing.PointF]::new($Size * 0.81, $Size * 0.53),
            $outerBottom)
        $path.AddBezier(
            $outerBottom,
            [System.Drawing.PointF]::new($Size * 0.69, $Size * 0.65),
            [System.Drawing.PointF]::new($Size * 0.58, $Size * 0.69),
            $centerBottom)
    }
    else {
        $outerTop = [System.Drawing.PointF]::new($Size * 0.21, $Size * 0.26)
        $outerBottom = [System.Drawing.PointF]::new($Size * 0.20, $Size * 0.65)
        $path.AddBezier(
            $centerTop,
            [System.Drawing.PointF]::new($Size * 0.41, $Size * 0.27),
            [System.Drawing.PointF]::new($Size * 0.30, $Size * 0.25),
            $outerTop)
        $path.AddBezier(
            $outerTop,
            [System.Drawing.PointF]::new($Size * 0.19, $Size * 0.38),
            [System.Drawing.PointF]::new($Size * 0.19, $Size * 0.53),
            $outerBottom)
        $path.AddBezier(
            $outerBottom,
            [System.Drawing.PointF]::new($Size * 0.31, $Size * 0.65),
            [System.Drawing.PointF]::new($Size * 0.42, $Size * 0.69),
            $centerBottom)
    }

    $path.CloseFigure()
    return $path
}

function New-IconFrame {
    param([int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

        $inset = [single]($Size * 0.035)
        $bounds = [System.Drawing.RectangleF]::new(
            $inset,
            $inset,
            [single]($Size - ($inset * 2)),
            [single]($Size - ($inset * 2)))
        $backgroundPath = New-RoundedRectanglePath -Bounds $bounds -Radius ([single]($Size * 0.205))
        $backgroundBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            $bounds,
            [System.Drawing.ColorTranslator]::FromHtml('#075985'),
            [System.Drawing.ColorTranslator]::FromHtml('#0EA5E9'),
            45.0)

        $leftPage = New-BookPagePath -Size $Size -RightPage $false
        $rightPage = New-BookPagePath -Size $Size -RightPage $true
        $pageBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(250, 255, 255, 255))
        $spinePen = [System.Drawing.Pen]::new(
            [System.Drawing.Color]::FromArgb(220, 125, 211, 252),
            [single][Math]::Max(1.0, $Size * 0.025))
        $spinePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $spinePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

        try {
            $graphics.FillPath($backgroundBrush, $backgroundPath)
            $graphics.FillPath($pageBrush, $leftPage)
            $graphics.FillPath($pageBrush, $rightPage)
            $graphics.DrawLine(
                $spinePen,
                [single]($Size * 0.50),
                [single]($Size * 0.34),
                [single]($Size * 0.50),
                [single]($Size * 0.73))

            if ($Size -ge 32) {
                $detailPen = [System.Drawing.Pen]::new(
                    [System.Drawing.Color]::FromArgb(90, 2, 132, 199),
                    [single][Math]::Max(1.0, $Size * 0.012))
                try {
                    $graphics.DrawLine($detailPen, $Size * 0.29, $Size * 0.45, $Size * 0.43, $Size * 0.48)
                    $graphics.DrawLine($detailPen, $Size * 0.29, $Size * 0.54, $Size * 0.43, $Size * 0.57)
                    $graphics.DrawLine($detailPen, $Size * 0.57, $Size * 0.48, $Size * 0.71, $Size * 0.45)
                    $graphics.DrawLine($detailPen, $Size * 0.57, $Size * 0.57, $Size * 0.71, $Size * 0.54)
                }
                finally {
                    $detailPen.Dispose()
                }
            }
        }
        finally {
            $spinePen.Dispose()
            $pageBrush.Dispose()
            $rightPage.Dispose()
            $leftPage.Dispose()
            $backgroundBrush.Dispose()
            $backgroundPath.Dispose()
        }

        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return $stream.ToArray()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = [System.Collections.Generic.List[object]]::new()
foreach ($size in $sizes) {
    $frames.Add((New-IconFrame -Size $size))
}

$iconPath = Join-Path $OutputDirectory 'HNovaReader.ico'
$previewPath = Join-Path $OutputDirectory 'HNovaReader.png'

$file = [System.IO.FileStream]::new($iconPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)

    $offset = 6 + (16 * $frames.Count)
    for ($index = 0; $index -lt $frames.Count; $index++) {
        $size = $sizes[$index]
        $data = [byte[]]$frames[$index]
        $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
        $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$data.Length)
        $writer.Write([uint32]$offset)
        $offset += $data.Length
    }

    foreach ($frame in $frames) {
        $writer.Write([byte[]]$frame)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

[System.IO.File]::WriteAllBytes($previewPath, [byte[]]$frames[$frames.Count - 1])

Write-Output "Generated $iconPath"
Write-Output "Generated $previewPath"
