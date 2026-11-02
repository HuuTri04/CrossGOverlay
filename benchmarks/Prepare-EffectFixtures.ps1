<# Prepare image/effect profiles in ignored benchmark data, never in user settings. #>
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$results=Join-Path $PSScriptRoot 'results'
$root=Join-Path $results 'profiling-app-data\effects'
$sourceSettings=Join-Path $results 'app-data\settings.json'
$sourcePresets=Join-Path $results 'app-data\presets'
if(-not(Test-Path $sourceSettings)){throw 'Seed benchmark settings missing.'}
New-Item -ItemType Directory -Path $root -Force|Out-Null
$png=Join-Path $root 'background-1600x900.png'
$gif=Join-Path $root 'crosshair-64x64-two-frame.gif'

if(-not(Test-Path $png)){
    Add-Type -AssemblyName System.Drawing
    $bitmap=[Drawing.Bitmap]::new(1600,900)
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    try{
        $graphics.Clear([Drawing.Color]::FromArgb(24,36,44))
        for($i=0;$i -lt 20;$i++){
            $brush=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(35+$i*4,40+$i*3,60+$i*2))
            try{$graphics.FillRectangle($brush,0,$i*45,1600,45)}finally{$brush.Dispose()}
        }
        $bitmap.Save($png,[Drawing.Imaging.ImageFormat]::Png)
    }finally{$graphics.Dispose();$bitmap.Dispose()}
}
if(-not(Test-Path $gif)){
    Add-Type -AssemblyName PresentationCore
    Add-Type -AssemblyName WindowsBase
    $encoder=[Windows.Media.Imaging.GifBitmapEncoder]::new()
    foreach($color in @(@(0,255,0),@(255,0,0))){
        $bytes=[byte[]]::new(64*64*4)
        for($y=0;$y -lt 64;$y++){
            for($x=0;$x -lt 64;$x++){
                if([math]::Abs($x-32) -le 2 -or [math]::Abs($y-32) -le 2){
                    $offset=($y*64+$x)*4
                    $bytes[$offset]=$color[2]
                    $bytes[$offset+1]=$color[1]
                    $bytes[$offset+2]=$color[0]
                    $bytes[$offset+3]=255
                }
            }
        }
        $source=[Windows.Media.Imaging.BitmapSource]::Create(64,64,96,96,
            [Windows.Media.PixelFormats]::Bgra32,$null,$bytes,64*4)
        $source.Freeze()
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($source))
    }
    $stream=[IO.File]::Create($gif)
    try{$encoder.Save($stream)}finally{$stream.Dispose()}
}

foreach($variant in @('standard','normal','background','background-60','background-144','background-contrast','gif')){
    $dir=Join-Path $root $variant
    $presetDir=Join-Path $dir 'presets'
    New-Item -ItemType Directory -Path $presetDir -Force|Out-Null
    $settings=Get-Content $sourceSettings -Raw -Encoding UTF8|ConvertFrom-Json
    $settings.StartMinimizedToTray=($variant -eq 'standard' -or $variant -eq 'gif')
    $settings.ShowOnlyInMatchedGames=$false
    $settings.OverlayEnabled=$true
    $settings.Language='en'
    $settings.EnableSmartContrast=($variant -eq 'background-contrast')
    $settings.CustomAppBgPath=if($variant -like 'background*'){$png}else{$null}
    $settings.AppBgBlurRadius=10
    if($variant -eq 'background-60'){$settings.UiTargetFps=60}
    if($variant -eq 'background-144'){$settings.UiTargetFps=144}
    $settings|ConvertTo-Json -Depth 16|Set-Content (Join-Path $dir 'settings.json') -Encoding UTF8
    Get-ChildItem $sourcePresets -File|Copy-Item -Destination $presetDir -Force
    if($variant -eq 'gif'){
        $activeFile=Join-Path $presetDir ("$($settings.ActivePresetId).json")
        if(-not(Test-Path $activeFile)){throw "Active preset missing: $activeFile"}
        $profile=Get-Content $activeFile -Raw -Encoding UTF8|ConvertFrom-Json
        $profile.Type='Image'
        $profile.Image.FilePath=$gif
        $profile|ConvertTo-Json -Depth 16|Set-Content $activeFile -Encoding UTF8
    }
}
Write-Host "[Profiler] Fixtures prepared in $root (1600x900 PNG, 2-frame 64x64 GIF)."
