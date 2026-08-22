param(
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $projectRoot "src\DesktopOrganizer.App\Assets\DesktopOrganizer.ico"
}
$previewPath = Join-Path $projectRoot "src\DesktopOrganizer.App\Assets\DesktopOrganizer.png"
$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $previewPath) -Force | Out-Null

function Add-RoundedRectangle {
    param(
        [System.Drawing.Drawing2D.GraphicsPath]$Path,
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )
    $diameter = $Radius * 2
    $Path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $Path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $Path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $Path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $Path.CloseFigure()
}

function New-IconBitmap {
    param([int]$Size)
    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $scale = $Size / 256.0

        $backgroundPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
        Add-RoundedRectangle $backgroundPath (12 * $scale) (12 * $scale) (232 * $scale) (232 * $scale) (42 * $scale)
        $backgroundBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 35, 42, 47))
        $graphics.FillPath($backgroundBrush, $backgroundPath)

        $containerPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
        Add-RoundedRectangle $containerPath (43 * $scale) (48 * $scale) (170 * $scale) (160 * $scale) (23 * $scale)
        $containerBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 214, 239, 235))
        $containerPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 31, 151, 136), [Math]::Max(1.0, 13 * $scale))
        $containerPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $graphics.FillPath($containerBrush, $containerPath)
        $graphics.DrawPath($containerPen, $containerPath)

        $tileBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 35, 42, 47))
        foreach ($point in @(@(71, 78), @(137, 78), @(71, 143))) {
            $tilePath = [System.Drawing.Drawing2D.GraphicsPath]::new()
            Add-RoundedRectangle $tilePath ($point[0] * $scale) ($point[1] * $scale) (43 * $scale) (43 * $scale) (9 * $scale)
            $graphics.FillPath($tileBrush, $tilePath)
            $tilePath.Dispose()
        }
        foreach ($x in @(142, 160, 178)) {
            $graphics.FillEllipse($tileBrush, $x * $scale, 159 * $scale, 10 * $scale, 10 * $scale)
        }

        $backgroundPath.Dispose()
        $backgroundBrush.Dispose()
        $containerPath.Dispose()
        $containerBrush.Dispose()
        $containerPen.Dispose()
        $tileBrush.Dispose()
        return $bitmap
    }
    finally {
        $graphics.Dispose()
    }
}

$sizes = @(16, 32, 48, 256)
$images = @()
foreach ($size in $sizes) {
    $bitmap = New-IconBitmap $size
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
    $stream.Dispose()
    if ($size -eq 256) {
        $bitmap.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $bitmap.Dispose()
}

$fileStream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create)
$writer = [System.IO.BinaryWriter]::new($fileStream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)
    $offset = 6 + (16 * $images.Count)
    foreach ($image in $images) {
        $dimension = if ($image.Size -eq 256) { [byte]0 } else { [byte]$image.Size }
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$image.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $image.Bytes.Length
    }
    foreach ($image in $images) {
        $writer.Write($image.Bytes)
    }
}
finally {
    $writer.Dispose()
    $fileStream.Dispose()
}

[pscustomobject]@{
    Icon = $OutputPath
    Preview = $previewPath
    Sizes = ($sizes -join ", ")
} | Format-List
