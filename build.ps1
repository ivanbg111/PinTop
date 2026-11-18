# Compila PinTop con el compilador de C# que ya trae Windows (.NET Framework 4.8).
# No hace falta instalar nada. Resultado: bin\PinTop.exe
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "No se encontró csc.exe ($csc). Hace falta .NET Framework 4.8." }

New-Item -ItemType Directory -Force -Path bin | Out-Null

& $csc /nologo /target:winexe /optimize+ /warn:4 `
    /out:bin\PinTop.exe `
    /win32icon:assets\pintop.ico `
    /win32manifest:src\app.manifest `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    src\*.cs

if ($LASTEXITCODE -ne 0) { throw "La compilación falló." }
Write-Host "OK -> $(Join-Path $PSScriptRoot 'bin\PinTop.exe')"
