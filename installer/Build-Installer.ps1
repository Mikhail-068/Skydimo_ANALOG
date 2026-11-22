[CmdletBinding()]
param(
    [string]$MakeNsis = 'makensis.exe',
    [string]$OutputDirectory,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$project = Join-Path (Split-Path -Parent $PSScriptRoot) 'Kontur.csproj'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
if (!$OutputDirectory) {
    $OutputDirectory = Join-Path (Split-Path -Parent $project) "release-$version"
}
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (!$SkipPublish) {
    dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $output
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось собрать автономный EXE.' }
}

$app = (Resolve-Path -LiteralPath (Join-Path $output 'Kontur.exe')).Path
$licenses = (Resolve-Path -LiteralPath (Join-Path $output 'licenses')).Path
$setup = Join-Path $output 'Kontur-Setup-x64.exe'
$compiler = Get-Command $MakeNsis -CommandType Application
$compilerDirectory = Split-Path -Parent $compiler.Source
$notice = Join-Path $compilerDirectory 'COPYING'
if (!(Test-Path -LiteralPath $notice -PathType Leaf)) {
    $notice = Join-Path (Split-Path -Parent $compilerDirectory) 'COPYING'
}
$notice = (Resolve-Path -LiteralPath $notice).Path
$arguments = @(
    "/DAPP_FILE=$app"
    "/DAPP_VERSION=$version"
    "/DOUTPUT_FILE=$setup"
    "/DLICENSE_DIR=$licenses"
    "/DNSIS_NOTICE=$notice"
    (Join-Path $PSScriptRoot 'Kontur.nsi')
)
& $compiler.Source @arguments
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $setup -PathType Leaf)) {
    throw 'NSIS не собрал установщик.'
}
$hashes = Get-FileHash -LiteralPath $app,$setup
$lines = $hashes | ForEach-Object { '{0}  {1}' -f $_.Hash.ToLowerInvariant(), (Split-Path -Leaf $_.Path) }
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'), [string[]]$lines)
Get-Item -LiteralPath $app,$setup
