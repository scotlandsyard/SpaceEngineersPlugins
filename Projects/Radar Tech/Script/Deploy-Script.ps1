<#
.SYNOPSIS
    Copies the Radar Tech PB script into the game's local script folder.
.DESCRIPTION
    Takes the "#region Script" part of Program.cs, removes the class indentation and writes it to
    %AppData%\SpaceEngineers\IngameScripts\local\Radar Tech\Script.cs (plus thumb.png if there is one).
    In game it then shows up under Edit > Browse Scripts in a programmable block, and can be
    published to the workshop from there.
#>
param(
    [string]$Target = (Join-Path $env:APPDATA 'SpaceEngineers\IngameScripts\local\Radar Tech')
)
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'Program.cs'
$lines = Get-Content $source -Encoding UTF8
$start = ($lines | Select-String -Pattern '^\s*#region Script\s*$' | Select-Object -First 1).LineNumber
$end = ($lines | Select-String -Pattern '^\s*#endregion // Script\s*$' | Select-Object -First 1).LineNumber
if (-not $start -or -not $end -or $end -le $start) { throw 'Script markers not found in Program.cs' }

$body = $lines[$start..($end - 2)] | ForEach-Object { if ($_.StartsWith('        ')) { $_.Substring(8) } else { $_.TrimStart() } }
$text = ($body -join "`n") + "`n"
if ($text.Length -gt 100000) { throw "Script is $($text.Length) characters; the game's limit is 100000" }

New-Item -ItemType Directory -Force $Target | Out-Null
[IO.File]::WriteAllText((Join-Path $Target 'Script.cs'), $text, (New-Object Text.UTF8Encoding $false))
$thumb = Join-Path $PSScriptRoot 'thumb.png'
if (Test-Path $thumb) { Copy-Item $thumb $Target -Force }
"Wrote $($text.Length) characters to $Target"
