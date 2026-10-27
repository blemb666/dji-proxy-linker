# Сборка DjiProxyLinker.exe без Visual Studio и без установки чего-либо:
# используется csc.exe из .NET Framework 4.x, который есть в любой Windows 10/11.
#
#   powershell -ExecutionPolicy Bypass -File build.ps1 [-Version 1.2.3] [-Installer | -InstallerOnly]
#
# -Installer     дополнительно собирает установщик (нужен Inno Setup 6: https://jrsoftware.org/isdl.php)
# -InstallerOnly собирает только установщик из уже готового dist\DjiProxyLinker.exe
#                (в CI — после того, как exe подписан)

param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0',
    [switch]$Installer,
    [switch]$InstallerOnly
)

$ErrorActionPreference = 'Stop'
$root  = $PSScriptRoot
$fw    = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc   = Join-Path $fw 'csc.exe'
$wpf   = Join-Path $fw 'WPF'
$build = Join-Path $root 'build'
$dist  = Join-Path $root 'dist'
$out   = Join-Path $dist 'DjiProxyLinker.exe'
$ico   = Join-Path $build 'app.ico'
New-Item -ItemType Directory -Force $build, $dist | Out-Null

# 1. Иконка: рендерим векторный src\Icon.xaml в .ico (PNG-кадры 16…256)
function Build-Icon {
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
}

# 2. Версия и свойства файла, 3. компиляция
function Build-Exe {
    $assemblyInfo = Join-Path $build 'AssemblyInfo.cs'
    @"
using System.Reflection;
[assembly: AssemblyTitle("DJI Proxy Linker")]
[assembly: AssemblyProduct("DJI Proxy Linker")]
[assembly: AssemblyDescription("Links DJI .LRF proxy files for DaVinci Resolve")]
[assembly: AssemblyCopyright("Apache License 2.0")]
[assembly: AssemblyVersion("$Version.0")]
[assembly: AssemblyFileVersion("$Version.0")]
"@ | Set-Content -Encoding UTF8 $assemblyInfo

    & $csc /nologo /codepage:65001 /optimize+ /target:winexe /langversion:5 `
        "/out:$out" "/win32icon:$ico" `
        "/lib:$wpf" /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll `
        "/resource:$(Join-Path $root 'src\MainWindow.xaml'),MainWindow.xaml" `
        "/resource:$(Join-Path $root 'src\LinkLocalProxies.lua'),LinkLocalProxies.lua" `
        (Join-Path $root 'src\*.cs') $assemblyInfo
    if ($LASTEXITCODE -ne 0) { throw "csc завершился с ошибкой $LASTEXITCODE" }
    Write-Host "OK: $out v$Version ($([math]::Round((Get-Item $out).Length / 1KB)) KB)"
}

# 4. Установщик
function Build-Installer {
    if (-not (Test-Path $out)) { throw "Нет $out — сначала соберите exe" }
    $iscc = @(
        (Get-Command iscc.exe -ErrorAction SilentlyContinue | ForEach-Object Source),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) { throw 'Не найден Inno Setup 6 (ISCC.exe): https://jrsoftware.org/isdl.php' }

    & $iscc /Q "/DAppVersion=$Version" "/O$dist" (Join-Path $root 'installer\DjiProxyLinker.iss')
    if ($LASTEXITCODE -ne 0) { throw "ISCC завершился с ошибкой $LASTEXITCODE" }
    Write-Host "OK: $(Join-Path $dist "DjiProxyLinker-Setup-$Version.exe")"
}

if (-not $InstallerOnly -or -not (Test-Path $ico)) { Build-Icon }
if (-not $InstallerOnly) { Build-Exe }
if ($Installer -or $InstallerOnly) { Build-Installer }
