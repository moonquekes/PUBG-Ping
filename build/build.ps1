$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$wpf = Join-Path $framework 'WPF'
$references = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Runtime.Serialization.dll', 'System.Xml.dll', 'System.Xaml.dll')
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output', ('/out:' + (Join-Path $root 'PUBG PING.exe')), ('/win32icon:' + (Join-Path $root 'source\app.ico')), ('/win32manifest:' + (Join-Path $root 'source\app.manifest')))
foreach ($name in $references) { $arguments += '/reference:' + (Join-Path $framework $name) }
foreach ($name in @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll')) { $arguments += '/reference:' + (Join-Path $wpf $name) }
$arguments += Join-Path $root 'source\TacticalPing.cs'
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed: $LASTEXITCODE" }
Write-Host ('Built: ' + (Join-Path $root 'PUBG PING.exe'))
