param([string]$Destination)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$cache = Join-Path $root '.tools'
New-Item -ItemType Directory -Force $cache | Out-Null
$archive = Join-Path $cache 'vlc-3.0.24.zip'
$expected = 'fcf30850371ad10c9373cc4f0f4501e7dee49e3e9ae9f20c72fb2661a1ca6323'
if (!(Test-Path $archive)) { Invoke-WebRequest 'https://downloads.videolan.org/videolan/vlc/3.0.24/win64/vlc-3.0.24-win64.zip' -OutFile $archive -TimeoutSec 180 }
if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'O arquivo oficial do VLC falhou na verificação SHA-256.' }
$extracted = Join-Path $cache 'vlc\vlc-3.0.24'
if (!(Test-Path (Join-Path $extracted 'libvlc.dll'))) { Expand-Archive $archive (Join-Path $cache 'vlc') -Force }
New-Item -ItemType Directory -Force $Destination | Out-Null
Copy-Item (Join-Path $extracted 'libvlc.dll'),(Join-Path $extracted 'libvlccore.dll'),(Join-Path $extracted 'COPYING.txt'),(Join-Path $extracted 'AUTHORS.txt') $Destination -Force
Copy-Item (Join-Path $extracted 'plugins') $Destination -Recurse -Force
$source = Join-Path $cache 'vlc-3.0.24-source.tar.xz'
if (!(Test-Path $source)) { Invoke-WebRequest 'https://downloads.videolan.org/videolan/vlc/3.0.24/vlc-3.0.24.tar.xz' -OutFile $source -TimeoutSec 180 }
if ((Get-FileHash $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'e7cab503d1d7d5849b89d2cf0e1ee60d0ef6d012407791b644b9cfc0cc225fdf') { throw 'O código-fonte do VLC falhou na verificação SHA-256.' }
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item $source (Join-Path $dist 'vlc-3.0.24-source.tar.xz') -Force
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $Destination -Force
