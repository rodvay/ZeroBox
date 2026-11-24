$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

Add-Type -AssemblyName System.Drawing

function New-ZeroBoxIcon {
    param([string]$OutPath)
    $sizes = @(16, 32, 48, 256)
    $pngList = New-Object System.Collections.Generic.List[byte[]]

    foreach ($sz in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap($sz, $sz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)

        $pad = [float]($sz * 0.06)
        $rect = New-Object System.Drawing.RectangleF($pad, $pad, ($sz - $pad * 2), ($sz - $pad * 2))
        $rad = [float]($sz * 0.24); $d = $rad * 2.0

        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
        $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
        $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
        $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
        $path.CloseFigure()

        $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            $rect, [System.Drawing.Color]::FromArgb(255, 18, 30, 48), [System.Drawing.Color]::FromArgb(255, 10, 14, 22), 45.0)
        $g.FillPath($bgBrush, $path)

        $bdPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 56, 189, 248), [Math]::Max(1.2, $sz * 0.045))
        $g.DrawPath($bdPen, $path)

        # 2x2 modular grid tiles inside the toolbox mark
        $u = $sz * 0.22; $gap = $sz * 0.08
        $start = ($sz - ($u * 2 + $gap)) / 2.0
        $cols = @(
            [System.Drawing.Color]::FromArgb(255, 56, 189, 248),
            [System.Drawing.Color]::FromArgb(255, 52, 211, 153),
            [System.Drawing.Color]::FromArgb(255, 167, 139, 250),
            [System.Drawing.Color]::FromArgb(255, 251, 191, 36)
        )
        $idx = 0
        for ($r = 0; $r -lt 2; $r++) {
            for ($c = 0; $c -lt 2; $c++) {
                $tx = [float]($start + $c * ($u + $gap))
                $ty = [float]($start + $r * ($u + $gap))
                $tb = New-Object System.Drawing.SolidBrush($cols[$idx])
                $g.FillRectangle($tb, $tx, $ty, [float]$u, [float]$u)
                $tb.Dispose()
                $idx++
            }
        }

        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngList.Add($ms.ToArray())
        $ms.Dispose(); $bdPen.Dispose(); $bgBrush.Dispose(); $path.Dispose(); $g.Dispose(); $bmp.Dispose()
    }

    $fs = [System.IO.File]::Create($OutPath)
    $bw = New-Object System.IO.BinaryWriter($fs)
    try {
        $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
        $offset = 6 + (16 * $sizes.Count)
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $sz = $sizes[$i]; $bytes = $pngList[$i]
            $dim = if ($sz -ge 256) { [byte]0 } else { [byte]$sz }
            $bw.Write($dim); $bw.Write($dim); $bw.Write([byte]0); $bw.Write([byte]0)
            $bw.Write([uint16]1); $bw.Write([uint16]32)
            $bw.Write([uint32]$bytes.Length); $bw.Write([uint32]$offset)
            $offset += $bytes.Length
        }
        foreach ($bytes in $pngList) { $bw.Write($bytes) }
    } finally { $bw.Dispose(); $fs.Dispose() }
}

Write-Host "Generating app.ico..."
New-ZeroBoxIcon -OutPath (Join-Path $scriptDir "app.ico")

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (-not (Test-Path $csc)) { throw "csc.exe not found under $env:WINDIR\Microsoft.NET" }

Write-Host "Compiling ZeroBox.exe..."
& $csc `
    /nologo `
    /target:winexe `
    /optimize+ `
    /platform:anycpu `
    /codepage:65001 `
    /utf8output `
    /win32icon:"$scriptDir\app.ico" `
    /win32manifest:"$scriptDir\app.manifest" `
    /reference:System.dll `
    /reference:System.Drawing.dll `
    /reference:System.IO.Compression.dll `
    /reference:System.IO.Compression.FileSystem.dll `
    /reference:System.Web.Extensions.dll `
    /reference:System.Windows.Forms.dll `
    /out:"$scriptDir\ZeroBox.exe" `
    "$scriptDir\ZeroNative.cs" `
    "$scriptDir\ModuleCatalog.cs" `
    "$scriptDir\ModuleManager.cs" `
    "$scriptDir\Program.cs"

if ($LASTEXITCODE -ne 0) { throw "Compilation failed (ExitCode=$LASTEXITCODE)" }

$exeInfo = Get-Item (Join-Path $scriptDir "ZeroBox.exe")
Write-Host ("Successfully built: {0} ({1:N1} KB)" -f $exeInfo.FullName, ($exeInfo.Length / 1024.0))
