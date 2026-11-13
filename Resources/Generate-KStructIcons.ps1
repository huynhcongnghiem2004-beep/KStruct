Add-Type -AssemblyName System.Drawing

$resourceDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Split-Path -Parent $resourceDir
$appPath = Join-Path $projectDir 'App.cs'
$ribbonDir = Join-Path $resourceDir 'Ribbon'
if (-not (Test-Path $appPath)) { throw "Không tìm thấy App.cs: $appPath" }
New-Item -ItemType Directory -Force -Path $ribbonDir | Out-Null

$navy = [System.Drawing.Color]::FromArgb(23, 48, 71)       # Navy #173047
$earth = [System.Drawing.Color]::FromArgb(176, 111, 76)    # Đất nung #B06F4C
$ivory = [System.Drawing.Color]::FromArgb(248, 244, 235)   # Ngà sáng
$transparent = [System.Drawing.Color]::Transparent

function New-Pen($color, [single]$width) {
    $pen = [System.Drawing.Pen]::new($color, $width)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    return $pen
}

function Draw-Line($g, $color, [single]$width, [single]$x1, [single]$y1, [single]$x2, [single]$y2) {
    $pen = New-Pen $color $width
    $g.DrawLine($pen, $x1, $y1, $x2, $y2)
    $pen.Dispose()
}

function New-RoundedRectanglePath([single]$x, [single]$y, [single]$width, [single]$height, [single]$radius) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Draw-RoundedRectangle($g, $pen, [single]$x, [single]$y, [single]$width, [single]$height, [single]$radius) {
    $path = New-RoundedRectanglePath $x $y $width $height $radius
    $g.DrawPath($pen, $path)
    $path.Dispose()
}

function Fill-RoundedRectangle($g, $brush, [single]$x, [single]$y, [single]$width, [single]$height, [single]$radius) {
    $path = New-RoundedRectanglePath $x $y $width $height $radius
    $g.FillPath($brush, $path)
    $path.Dispose()
}

function Get-Action($id) {
    $s = $id.ToLowerInvariant()
    if ($s -match 'delete|remove|explode') { return 'delete' }
    if ($s -match 'update|refresh|match') { return 'update' }
    if ($s -match 'setting|config|manage|manager|parameter') { return 'settings' }
    if ($s -match 'select|filter') { return 'select' }
    if ($s -match 'tag|mark|number') { return 'tag' }
    if ($s -match 'dimension|length') { return 'dimension' }
    if ($s -match 'export') { return 'export' }
    if ($s -match 'copy') { return 'copy' }
    if ($s -match 'hide|visibility') { return 'hide' }
    if ($s -match 'color|paint') { return 'color' }
    if ($s -match 'check|preview|audit') { return 'check' }
    if ($s -match 'create|draw|place|reinforce|add|rebar') { return 'add' }
    if ($s -match 'split|bend|extend|edit') { return 'edit' }
    if ($s -match 'optimize') { return 'check' }
    return 'none'
}

function Get-IconFamily($id, $legacyFamily) {
    $name = $id.ToLowerInvariant()
    if ($name -match 'slab|floor|uboot|bubble|mushroom') { return 'Floor' }
    if ($name -match 'wall|pilaster') { return 'Wall' }
    if ($name -match 'stair') { return 'Stair' }
    if ($name -match 'footing|foundation') { return 'Footing' }
    if ($name -match 'column') { return 'Column' }
    if ($name -match 'beam|lintel') { return 'Beam' }
    if ($name -match 'schedule|count|cutlist') { return 'Schedule' }
    if ($name -match 'tag|number|drawings|sheet') { return 'Document' }
    if ($name -match 'setting|parameter|manager') { return 'Settings' }
    switch ($legacyFamily.ToLowerInvariant()) {
        'column' { return 'Column' }
        'beam' { return 'Beam' }
        'floor' { return 'Floor' }
        'footing' { return 'Footing' }
        'settings' { return 'Settings' }
        default { return 'Rebar' }
    }
}

function Draw-Family($g, $family) {
    $line = New-Pen $ivory 2.0
    $accent = New-Pen $earth 2.0
    $fine = New-Pen $ivory 1.35
    $fill = [System.Drawing.SolidBrush]::new($ivory)
    switch ($family) {
        'Column' {
            Draw-RoundedRectangle $g $line 10 5 12 22 2
            Draw-Line $g $earth 1.8 13 7 13 24
            Draw-Line $g $earth 1.8 19 7 19 24
            Draw-Line $g $ivory 1.25 11 11 21 11
            Draw-Line $g $ivory 1.25 11 17 21 17
            Draw-Line $g $ivory 1.25 11 23 21 23
        }
        'Beam' {
            Draw-RoundedRectangle $g $line 5 10 22 12 2
            Draw-Line $g $earth 1.8 7 14 25 14
            Draw-Line $g $earth 1.8 7 19 25 19
            Draw-Line $g $ivory 1.2 12 12 12 20
            Draw-Line $g $ivory 1.2 20 12 20 20
        }
        'Floor' {
            $points = [System.Drawing.Point[]]@(
                [System.Drawing.Point]::new(5, 12), [System.Drawing.Point]::new(20, 7),
                [System.Drawing.Point]::new(27, 12), [System.Drawing.Point]::new(12, 17))
            $g.DrawPolygon($line, $points)
            Draw-Line $g $earth 1.8 8 15 8 23
            Draw-Line $g $earth 1.8 12 17 12 25
            Draw-Line $g $ivory 1.6 12 25 27 20
            Draw-Line $g $ivory 1.6 27 12 27 20
            Draw-Line $g $ivory 1.1 12 12 22 9
            Draw-Line $g $ivory 1.1 16 16 24 13
        }
        'Footing' {
            $points = [System.Drawing.Point[]]@(
                [System.Drawing.Point]::new(5, 19), [System.Drawing.Point]::new(27, 19),
                [System.Drawing.Point]::new(24, 26), [System.Drawing.Point]::new(8, 26))
            $g.DrawPolygon($line, $points)
            Draw-RoundedRectangle $g $line 12 6 8 13 1
            Draw-Line $g $earth 1.8 8 22 24 22
            Draw-Line $g $earth 1.5 12 24 12 27
            Draw-Line $g $earth 1.5 20 24 20 27
        }
        'Wall' {
            Draw-RoundedRectangle $g $line 6 8 20 16 1
            Draw-Line $g $earth 1.7 6 15 26 15
            Draw-Line $g $ivory 1.3 16 8 16 15
            Draw-Line $g $ivory 1.3 11 15 11 24
            Draw-Line $g $ivory 1.3 21 15 21 24
        }
        'Stair' {
            Draw-Line $g $ivory 2 5 25 11 25
            Draw-Line $g $ivory 2 11 25 11 20
            Draw-Line $g $ivory 2 11 20 17 20
            Draw-Line $g $ivory 2 17 20 17 15
            Draw-Line $g $ivory 2 17 15 23 15
            Draw-Line $g $ivory 2 23 15 23 9
            Draw-Line $g $earth 1.8 7 13 24 6
        }
        'Settings' {
            $g.DrawEllipse($line, 8, 8, 16, 16)
            $g.DrawEllipse($accent, 13, 13, 6, 6)
            for ($i = 0; $i -lt 8; $i++) {
                $a = [Math]::PI * $i / 4
                Draw-Line $g $ivory 1.7 ([single](16 + [Math]::Cos($a) * 9)) ([single](16 + [Math]::Sin($a) * 9)) ([single](16 + [Math]::Cos($a) * 11.5)) ([single](16 + [Math]::Sin($a) * 11.5))
            }
        }
        'Schedule' { Draw-Document $g $line $ivory $earth }
        'Document' { Draw-Document $g $line $ivory $earth }
        default {
            Draw-Line $g $ivory 2 7 7 7 21
            Draw-Line $g $ivory 2 7 21 10 24
            Draw-Line $g $ivory 2 10 24 22 24
            Draw-Line $g $ivory 2 22 24 25 21
            Draw-Line $g $ivory 2 25 21 25 8
            Draw-Line $g $earth 1.8 11 10 11 20
            Draw-Line $g $earth 1.8 11 20 21 20
            Draw-Line $g $earth 1.8 21 20 21 12
        }
    }
    $line.Dispose(); $accent.Dispose(); $fine.Dispose(); $fill.Dispose()
}

function Draw-Document($g, $line, $ivoryColor, $earthColor) {
    Draw-RoundedRectangle $g $line 8 5 16 22 2
    Draw-Line $g $earthColor 1.8 12 11 20 11
    Draw-Line $g $ivoryColor 1.2 12 16 20 16
    Draw-Line $g $ivoryColor 1.2 12 21 18 21
}

function Draw-Action($g, $action) {
    if ($action -eq 'none') {
        $brush = [System.Drawing.SolidBrush]::new($earth)
        $g.FillEllipse($brush, 24, 3, 5, 5)
        $brush.Dispose()
        return
    }
    $badge = [System.Drawing.SolidBrush]::new($earth)
    Fill-RoundedRectangle $g $badge 19 19 11 11 2
    $badge.Dispose()
    $w = 1.35
    switch ($action) {
        'delete' {
            Draw-Line $g $ivory $w 22 22 27 27; Draw-Line $g $ivory $w 27 22 22 27
        }
        'update' {
            $p = New-Pen $ivory $w; $g.DrawArc($p, 21, 21, 7, 7, 35, 285); $p.Dispose()
            Draw-Line $g $ivory $w 21 22 21 25; Draw-Line $g $ivory $w 21 22 24 22
        }
        'settings' {
            $p = New-Pen $ivory $w; $g.DrawEllipse($p, 21, 21, 7, 7); $p.Dispose()
            $p = New-Pen $ivory $w; $g.DrawEllipse($p, 23.3, 23.3, 2.4, 2.4); $p.Dispose()
        }
        'select' { $p = New-Pen $ivory $w; $g.DrawRectangle($p, 22, 22, 5, 5); $p.Dispose() }
        'tag' {
            $pts = [System.Drawing.Point[]]@([System.Drawing.Point]::new(22,22),[System.Drawing.Point]::new(27,22),[System.Drawing.Point]::new(27,25),[System.Drawing.Point]::new(24,28),[System.Drawing.Point]::new(22,26))
            $p = New-Pen $ivory $w; $g.DrawPolygon($p, $pts); $p.Dispose()
        }
        'dimension' {
            Draw-Line $g $ivory $w 22 25 28 25; Draw-Line $g $ivory $w 22 23 22 27; Draw-Line $g $ivory $w 28 23 28 27
        }
        'export' {
            Draw-Line $g $ivory $w 25 21 25 27; Draw-Line $g $ivory $w 23 25 25 27; Draw-Line $g $ivory $w 27 25 25 27
        }
        'copy' {
            $p = New-Pen $ivory $w; $g.DrawRectangle($p, 22, 22, 4, 4); $g.DrawRectangle($p, 24, 24, 4, 4); $p.Dispose()
        }
        'hide' {
            $p = New-Pen $ivory $w; $g.DrawEllipse($p, 22, 23, 6, 4); $p.Dispose(); Draw-Line $g $ivory $w 22 28 28 22
        }
        'color' {
            $brush = [System.Drawing.SolidBrush]::new($ivory)
            $g.FillEllipse($brush, 22, 22, 2, 2); $g.FillEllipse($brush, 26, 22, 2, 2); $g.FillEllipse($brush, 24, 26, 2, 2); $brush.Dispose()
        }
        'check' {
            Draw-Line $g $ivory ($w + .3) 22 25 24 27; Draw-Line $g $ivory ($w + .3) 24 27 28 22
        }
        'add' {
            Draw-Line $g $ivory $w 25 22 25 28; Draw-Line $g $ivory $w 22 25 28 25
        }
        default {
            Draw-Line $g $ivory $w 22 27 27 22; Draw-Line $g $ivory $w 25 22 27 22; Draw-Line $g $ivory $w 27 22 27 24
        }
    }
}

function Save-Icon($path, $family, $action, [int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.Clear($transparent)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.ScaleTransform($size / 32.0, $size / 32.0)
    $tile = [System.Drawing.SolidBrush]::new($navy)
    Fill-RoundedRectangle $g $tile 0.5 0.5 31 31 5
    $tile.Dispose()
    Draw-Family $g $family
    Draw-Action $g $action
    $g.Dispose()
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

$source = Get-Content $appPath -Raw
$icons = @{}
$addPattern = 'Add\([^;]*?"(?<id>KStruct_[^"]+)"\s*,\s*"[^"]*"\s*,\s*typeof\([^)]*\)\s*,\s*"(?<family>[^"]+)"\s*\)'
foreach ($match in [regex]::Matches($source, $addPattern)) {
    $icons[$match.Groups['id'].Value] = $match.Groups['family'].Value
}
$directPattern = 'FlatRibbonIcons\.Create\("(?<id>KStruct_[^"]+)"\s*,\s*"(?<family>[^"]+)"'
foreach ($match in [regex]::Matches($source, $directPattern)) {
    $icons[$match.Groups['id'].Value] = $match.Groups['family'].Value
}
if ($icons.Count -lt 50) {
    throw "Chỉ tìm thấy $($icons.Count) lệnh có icon trong App.cs; dừng để tránh sinh thiếu icon."
}

$families = @('Column','Beam','Floor','Footing','RebarColumn','RebarColumnV3','Settings','Wall','Stair','Schedule','Document','Rebar')
foreach ($family in $families) {
    $baseFamily = if ($family -in @('RebarColumn','RebarColumnV3')) { 'Column' } else { $family }
    foreach ($size in @(16,32)) {
        Save-Icon (Join-Path $resourceDir ($family + '_' + $size + '.png')) $baseFamily 'none' $size
    }
}

foreach ($entry in $icons.GetEnumerator()) {
    $id = $entry.Key
    $family = Get-IconFamily $id $entry.Value
    $action = Get-Action $id
    foreach ($size in @(16,32)) {
        Save-Icon (Join-Path $ribbonDir ($id + '_' + $size + '.png')) $family $action $size
    }
}

$files = @(Get-ChildItem $ribbonDir -Filter '*.png' -File)
$bytes = ($files | Measure-Object -Property Length -Sum).Sum
Write-Output "Generated $($icons.Count) command icons ($($files.Count) compact PNGs, $bytes bytes) in $ribbonDir."
