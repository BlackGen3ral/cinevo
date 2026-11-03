param([string]$Version = '1.0.0', [string]$InnoCompiler = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
 $dotnet = if (Test-Path '.tools\dotnet\dotnet.exe') { (Resolve-Path '.tools\dotnet\dotnet.exe').Path } else { 'dotnet' }
 & $dotnet run --project tests\Cinevo.Tests.csproj -c Release
 if ($LASTEXITCODE -ne 0) { throw 'Os testes falharam.' }
 & $dotnet publish Cinevo\Cinevo.csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -o dist\app
 if ($LASTEXITCODE -ne 0) { throw 'A compilação falhou.' }
 if (!$InnoCompiler) { $InnoCompiler = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "${env:ProgramFiles}\Inno Setup 7\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe", (Join-Path $root '.tools\inno\ISCC.exe')) | Where-Object { Test-Path $_ } | Select-Object -First 1 }
 if (!(Test-Path $InnoCompiler)) { throw 'Instale o Inno Setup 6 ou informe -InnoCompiler.' }
 & $InnoCompiler "/DAppVersion=$Version" installer\Cinevo.iss
 if ($LASTEXITCODE -ne 0) { throw 'O instalador falhou.' }
 $hash = (Get-FileHash dist\Cinevo-Setup.exe -Algorithm SHA256).Hash.ToLowerInvariant()
 [IO.File]::WriteAllText((Join-Path $root 'dist\SHA256SUMS.txt'), "$hash  Cinevo-Setup.exe`n")
} finally { Pop-Location }
