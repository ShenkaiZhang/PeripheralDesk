param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?$')]
    [string]$Version='0.5.1-preview',
    [string]$OutputDirectory=(Join-Path $PSScriptRoot 'dist'),
    [switch]$UseExistingBuild
)
$ErrorActionPreference='Stop'
if(-not $UseExistingBuild){
    Push-Location $PSScriptRoot
    try{& (Join-Path $PSScriptRoot 'Build.cmd');if($LASTEXITCODE -ne 0){throw 'Build failed'}}
    finally{Pop-Location}
}
$output=[IO.Path]::GetFullPath($OutputDirectory)
$stage=Join-Path $PSScriptRoot ('obj\package-staging-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage,$output -Force | Out-Null
foreach($name in @('PeripheralDesk.exe','README.txt','RELEASE_NOTES.md','GPW_FIX.md','VALIDATION.md','LICENSE-HaloBattery.txt','THIRD_PARTY_NOTICES.md')){
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $stage $name)
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $stage 'LICENSE.txt')
$zip=Join-Path $output ('PeripheralDesk-Windows-x64-v'+$Version+'.zip')
if(Test-Path -LiteralPath $zip){throw 'Output ZIP already exists; use a new output directory or version'}
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try{
    if($archive.Entries.Count -ne 8){throw 'Unexpected package entry count'}
    $stream=$archive.GetEntry('PeripheralDesk.exe').Open()
    $sha=[Security.Cryptography.SHA256]::Create()
    try{$hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}
    finally{$stream.Dispose();$sha.Dispose()}
    if($hash -ne (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'PeripheralDesk.exe')).Hash){throw 'Packaged EXE mismatch'}
}finally{$archive.Dispose()}
$zipHash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$sumPath=Join-Path $output 'SHA256SUMS.txt'
[IO.File]::WriteAllText($sumPath,$zipHash+'  '+[IO.Path]::GetFileName($zip)+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
Write-Output ('Package: '+$zip)
Write-Output ('SHA-256: '+$zipHash)
Write-Output ('Staging retained for inspection: '+$stage)
