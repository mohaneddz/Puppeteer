[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.7.0'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root "build-verification\publish\Puppeteer-$Version-win-x64"
$artifacts = Join-Path $root "build-verification\release\v$Version"

Remove-Item -LiteralPath $publish, $artifacts -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $publish, $artifacts | Out-Null

dotnet publish (Join-Path $root 'src\Puppeteer.App\Puppeteer.App.csproj') `
    --configuration Release --runtime win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version -p:AssemblyVersion="$Version.0" -p:FileVersion="$Version.0" `
    --output $publish
if ($LASTEXITCODE) { throw 'Publishing failed.' }

$zip = Join-Path $artifacts "Puppeteer-$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip

$msi = Join-Path $artifacts "Puppeteer-$Version-win-x64-setup.msi"
wix --acceptEula wix7 build (Join-Path $root 'installer\Puppeteer.wxs') -arch x64 -d "PublishDir=$publish" -out $msi -pdbtype none
if ($LASTEXITCODE) { throw 'Installer build failed.' }

$hashes = Join-Path $artifacts 'SHA256SUMS.txt'
Get-ChildItem -LiteralPath $artifacts -File | Where-Object Name -ne 'SHA256SUMS.txt' |
    Get-FileHash -Algorithm SHA256 |
    ForEach-Object { "{0}  {1}" -f $_.Hash.ToLowerInvariant(), $_.Path.Substring($artifacts.Length + 1) } |
    Set-Content -LiteralPath $hashes -Encoding ascii

Get-ChildItem -LiteralPath $artifacts -File | Select-Object Name, Length
