# Сборка DjiProxyLinker.exe без Visual Studio и без установки чего-либо:
# используется csc.exe из .NET Framework 4.x, который есть в любой Windows 10/11.
#
#   powershell -ExecutionPolicy Bypass -File build.ps1

$ErrorActionPreference = 'Stop'
$root  = $PSScriptRoot
$fw    = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc   = Join-Path $fw 'csc.exe'
$wpf   = Join-Path $fw 'WPF'
$build = Join-Path $root 'build'
$dist  = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $build, $dist | Out-Null

# 1. Иконка: рендерим векторный src\Icon.xaml в .ico (PNG-кадры 16…256)
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$iconXaml = [IO.File]::ReadAllText((Join-Path $root 'src\Icon.xaml'))
$frames = foreach ($size in 16, 20, 24, 32, 40, 48, 64, 128, 256) {
    $box = New-Object Windows.Controls.Viewbox
    $box.Child = [Windows.Markup.XamlReader]::Parse($iconXaml)
    $box.Width = $size; $box.Height = $size
    $box.Measure((New-Object Windows.Size $size, $size))
    $box.Arrange((New-Object Windows.Rect 0, 0, $size, $size))
    $box.UpdateLayout()
    $rtb = New-Object Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($box)
    $png = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $png.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object IO.MemoryStream
    $png.Save($ms)
    [pscustomobject]@{ Size = $size; Bytes = $ms.ToArray() }
}

$ico = Join-Path $build 'app.ico'
$w = New-Object IO.BinaryWriter ([IO.File]::Create($ico))
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$f.Bytes.Length); $w.Write([uint32]$offset)
    $offset += $f.Bytes.Length
}
foreach ($f in $frames) { $w.Write($f.Bytes) }
$w.Close()
[IO.File]::WriteAllBytes((Join-Path $build 'icon-256.png'), $frames[-1].Bytes)

# 2. Компиляция
$out = Join-Path $dist 'DjiProxyLinker.exe'
& $csc /nologo /codepage:65001 /optimize+ /target:winexe /langversion:5 `
    "/out:$out" "/win32icon:$ico" `
    "/lib:$wpf" /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll `
    "/resource:$(Join-Path $root 'src\MainWindow.xaml'),MainWindow.xaml" `
    (Join-Path $root 'src\*.cs')
if ($LASTEXITCODE -ne 0) { throw "csc завершился с ошибкой $LASTEXITCODE" }

Write-Host "OK: $out ($([math]::Round((Get-Item $out).Length / 1KB)) KB)"
