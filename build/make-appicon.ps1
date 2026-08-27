# SS Pen 앱 아이콘 생성기 (WI-19 후속, 사용자 요청).
#
# 산출물 `src/SSPen/Assets/AppIcon.ico`는 저장소에 커밋되는 자산이다.
# 이 스크립트는 그 자산을 **재생성**하기 위한 도구이며 빌드 파이프라인에는 들어가지 않는다.
# 디자인은 툴바 로고 배지(ToolbarTheme.LogoBadge)·트레이 아이콘과 동일하다:
# 강조색(#FF00ADEF) 원 + 흰색 굵은 "S".
#
# 사용: powershell -ExecutionPolicy Bypass -File build/make-appicon.ps1

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$accent = [System.Drawing.Color]::FromArgb(0xFF, 0x00, 0xAD, 0xEF)
# 16~256: 탐색기·작업 표시줄·Alt+Tab·설정 앱이 각각 다른 크기를 고른다.
$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)

$repo = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $repo 'src\SSPen\Assets'
$icoPath = Join-Path $outDir 'AppIcon.ico'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# 크기마다 8배 슈퍼샘플링으로 그린 뒤 축소한다 — 작은 크기에서 원 가장자리와
# 글자 획 굵기가 뭉개지지 않게 하려는 조치.
function New-IconBitmap([int]$size) {
    $ss = 8
    $big = $size * $ss
    $canvas = New-Object System.Drawing.Bitmap $big, $big, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Round($big * 0.03)
    $diameter = $big - 2 * $pad
    $brush = New-Object System.Drawing.SolidBrush $accent
    $g.FillEllipse($brush, $pad, $pad, $diameter, $diameter)
    $brush.Dispose()

    $font = New-Object System.Drawing.Font 'Segoe UI', ($big * 0.58), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
    # 글꼴 메트릭상 시각 중심이 살짝 아래로 치우쳐 보이므로 광학 보정을 준다.
    $rect = New-Object System.Drawing.RectangleF 0, (-$big * 0.02), $big, $big
    $g.DrawString('S', $font, [System.Drawing.Brushes]::White, $rect, $fmt)
    $font.Dispose(); $fmt.Dispose(); $g.Dispose()

    $small = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $sg = [System.Drawing.Graphics]::FromImage($small)
    $sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $sg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $sg.Clear([System.Drawing.Color]::Transparent)
    $sg.DrawImage($canvas, (New-Object System.Drawing.Rectangle 0, 0, $size, $size))
    $sg.Dispose(); $canvas.Dispose()
    return $small
}

# ICO 컨테이너를 직접 조립한다. 각 엔트리는 PNG 압축(Vista+ 지원)이라
# 256px까지 별도 DIB 마스크 계산 없이 담을 수 있다.
$payloads = @()
$bitmaps = @()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    $bitmaps += $bmp
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $payloads += , $ms.ToArray()
    $ms.Dispose()
}

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)
$bw.Write([UInt16]0)                 # reserved
$bw.Write([UInt16]1)                 # type: 1 = icon
$bw.Write([UInt16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bytes = $payloads[$i]
    # 256은 바이트 0으로 기록하는 것이 ICO 규격이다.
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim)            # width
    $bw.Write([byte]$dim)            # height
    $bw.Write([byte]0)               # 팔레트 없음
    $bw.Write([byte]0)               # reserved
    $bw.Write([UInt16]1)             # color planes
    $bw.Write([UInt16]32)            # bits per pixel
    $bw.Write([UInt32]$bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($bytes in $payloads) { $bw.Write($bytes) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $out.ToArray())
$bw.Dispose(); $out.Dispose()

Write-Host "생성: $icoPath ($([math]::Round((Get-Item $icoPath).Length / 1KB, 1)) KB, 크기 $($sizes.Count)종)"

# 육안 검증용 미리보기: 회색 배경에 각 크기를 나란히 배치한다.
$previewW = [int](($sizes | Measure-Object -Sum).Sum) + 12 * $sizes.Count
$preview = New-Object System.Drawing.Bitmap ([int]$previewW), ([int]300)
$pg = [System.Drawing.Graphics]::FromImage($preview)
$pg.Clear([System.Drawing.Color]::FromArgb(255, 60, 60, 60))
$x = 6
foreach ($bmp in $bitmaps) {
    $pg.DrawImage($bmp, $x, [int]((300 - $bmp.Width) / 2))
    $x += $bmp.Width + 12
}
$pg.Dispose()
$previewPath = Join-Path $env:TEMP 'appicon-preview.png'
$preview.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()
foreach ($bmp in $bitmaps) { $bmp.Dispose() }
Write-Host "미리보기: $previewPath"
