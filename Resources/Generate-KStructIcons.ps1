Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ink = [System.Drawing.Color]::FromArgb(29, 55, 83)
$blue = [System.Drawing.Color]::FromArgb(40, 119, 207)
$teal = [System.Drawing.Color]::FromArgb(13, 157, 146)
$amber = [System.Drawing.Color]::FromArgb(235, 151, 43)
$pale = [System.Drawing.Color]::FromArgb(231, 241, 250)
$white = [System.Drawing.Color]::White

function New-Pen($color, [single]$width) { [System.Drawing.Pen]::new($color, $width) }
function New-Brush($color) { [System.Drawing.SolidBrush]::new($color) }
function Draw-Column($g) {
    $p=New-Pen $ink 2.8; $steel=New-Pen $blue 2.4; $tie=New-Pen $teal 2
    $b=New-Brush $pale; $g.FillRectangle($b,21,7,22,50); $g.DrawRectangle($p,21,7,22,50)
    foreach($x in @(26,38)){ $g.DrawLine($steel,$x,10,$x,54) }
    foreach($y in @(14,25,38,50)){ $g.DrawLine($tie,24,$y,40,$y) }
    $accent=New-Pen $amber 2.4; $g.DrawLine($accent,18,5,18,59); $g.DrawLine($accent,46,5,46,59)
}
function Draw-Beam($g) {
    $p=New-Pen $ink 2.6; $steel=New-Pen $blue 2.5; $lower=New-Pen $teal 2.2; $tie=New-Pen $amber 1.5
    $b=New-Brush $pale; $g.FillRectangle($b,8,20,48,23); $g.DrawRectangle($p,8,20,48,23)
    $g.FillRectangle($b,11,43,8,13); $g.FillRectangle($b,45,43,8,13); $g.DrawRectangle($p,11,43,8,13); $g.DrawRectangle($p,45,43,8,13)
    $g.DrawLine($steel,13,25,51,25); $g.DrawLine($lower,13,38,51,38)
    foreach($x in @(17,24,31,38,45)){ $g.DrawLine($tie,$x,27,$x,36) }
    $g.DrawLine((New-Pen $amber 2),5,59,59,59)
}
function Draw-Floor($g) {
    $p=New-Pen $ink 2.4; $grid=New-Pen $blue 1.4; $edge=New-Pen $teal 2
    $b=New-Brush $pale
    $poly=[System.Drawing.PointF[]]@([System.Drawing.PointF]::new(10,20),[System.Drawing.PointF]::new(42,12),[System.Drawing.PointF]::new(55,20),[System.Drawing.PointF]::new(23,29))
    $g.FillPolygon($b,$poly); $g.DrawPolygon($p,$poly)
    $g.DrawLine($edge,23,29,23,49); $g.DrawLine($edge,23,49,55,40); $g.DrawLine($p,55,20,55,40); $g.DrawLine($p,23,49,10,41); $g.DrawLine($p,10,20,10,41)
    foreach($t in @(0..3)){ $x=14+$t*9; $g.DrawLine($grid,$x,19-(($x-10)*0.25),$x+13,27-(($x-10)*0.25)); }
    foreach($t in @(0..2)){ $x=16+$t*8; $g.DrawLine($grid,$x,24,$x+24,18); }
}
function Draw-Footing($g) {
    $p=New-Pen $ink 2.5; $steel=New-Pen $blue 1.8; $rebar=New-Pen $amber 2
    $b=New-Brush $pale; $base=[System.Drawing.PointF[]]@([System.Drawing.PointF]::new(8,39),[System.Drawing.PointF]::new(53,39),[System.Drawing.PointF]::new(48,54),[System.Drawing.PointF]::new(13,54))
    $g.FillPolygon($b,$base); $g.DrawPolygon($p,$base)
    $g.DrawLine($p,8,39,18,32); $g.DrawLine($p,53,39,46,32); $g.DrawLine($p,18,32,46,32)
    $g.FillRectangle((New-Brush $teal),25,11,14,22); $g.DrawRectangle($p,25,11,14,22)
    $g.DrawLine($steel,17,42,47,42); $g.DrawLine($steel,15,47,49,47); $g.DrawLine($steel,20,51,20,41); $g.DrawLine($steel,29,52,29,41); $g.DrawLine($steel,38,52,38,41); $g.DrawLine($steel,46,50,46,41)
    $g.DrawLine($rebar,22,9,42,9)
}
function Draw-RebarColumn($g) {
    $outer=New-Pen $ink 2.6; $tie=New-Pen $teal 2; $bar=New-Brush $blue; $b=New-Brush $pale
    $g.FillRectangle($b,13,8,38,48); $g.DrawRectangle($outer,13,8,38,48)
    foreach($y in @(15,26,37,48)){ $g.DrawRectangle($tie,17,$y,30,7) }
    foreach($pt in @(@(18,13),@(46,13),@(18,51),@(46,51))){ $g.FillEllipse($bar,$pt[0]-2.5,$pt[1]-2.5,5,5) }
    $a=New-Pen $amber 2; $g.DrawLine($a,8,10,8,54); $g.DrawLine($a,56,10,56,54)
}
function Draw-RebarColumnV3($g) {
    $p=New-Pen $ink 2.6; $bar=New-Pen $blue 2.5; $tie=New-Pen $teal 2; $accent=New-Pen $amber 2
    $b=New-Brush $pale; $g.FillRectangle($b,15,10,34,44); $g.DrawRectangle($p,15,10,34,44)
    foreach($x in @(20,44)){ $g.DrawLine($bar,$x,8,$x,56) }
    foreach($y in @(16,27,38,49)){ $g.DrawLine($tie,18,$y,46,$y); $g.DrawLine($accent,18,$y,23,$y-5) }
    $g.DrawEllipse($p,24,19,16,16); $g.FillEllipse((New-Brush $amber),29,24,6,6)
}
function Draw-Settings($g) {
    $p=New-Pen $ink 2.6; $bluePen=New-Pen $blue 2.5; $tealPen=New-Pen $teal 2.5
    $pts=New-Object 'System.Drawing.PointF[]' 24
    for($i=0;$i -lt 24;$i++){ $angle=(-[Math]::PI/2)+$i*[Math]::PI/12; $r=if(($i%3)-eq 0){23}else{18}; $pts[$i]=[System.Drawing.PointF]::new([single](32+[Math]::Cos($angle)*$r),[single](32+[Math]::Sin($angle)*$r)) }
    $g.FillPolygon((New-Brush $pale),$pts); $g.DrawPolygon($p,$pts); $g.DrawEllipse($bluePen,23,23,18,18); $g.DrawEllipse($tealPen,28,28,8,8)
    $g.DrawLine($bluePen,4,55,60,55)
}

$designs = [ordered]@{
    Column = { param($g) Draw-Column $g }
    Beam = { param($g) Draw-Beam $g }
    Floor = { param($g) Draw-Floor $g }
    Footing = { param($g) Draw-Footing $g }
    RebarColumn = { param($g) Draw-RebarColumn $g }
    RebarColumnV3 = { param($g) Draw-RebarColumnV3 $g }
    Settings = { param($g) Draw-Settings $g }
}

foreach($entry in $designs.GetEnumerator()) {
    $large=[System.Drawing.Bitmap]::new(256,256,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g=[System.Drawing.Graphics]::FromImage($large); $g.Clear([System.Drawing.Color]::Transparent)
    $g.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode=[System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.ScaleTransform(4,4); & $entry.Value $g; $g.Dispose()
    foreach($size in @(32,16)) {
        $small=[System.Drawing.Bitmap]::new($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $sg=[System.Drawing.Graphics]::FromImage($small); $sg.Clear([System.Drawing.Color]::Transparent)
        $sg.SmoothingMode=[System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $sg.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $sg.PixelOffsetMode=[System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $sg.DrawImage($large,0,0,$size,$size); $sg.Dispose()
        $small.Save((Join-Path $root ($entry.Key+"_"+$size+".png")),[System.Drawing.Imaging.ImageFormat]::Png); $small.Dispose()
    }
    $large.Dispose()
}

Write-Output "Generated: $($designs.Keys -join ', ') at 16px and 32px."
