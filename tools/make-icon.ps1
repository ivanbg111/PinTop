# Regenera assets\pintop.ico con el mismo dibujo que usa la app (Glyphs/Icons en src\Support.cs)
# y vuelve a compilar para que el .exe lleve el icono nuevo.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $root 'build.ps1')
& (Join-Path $root 'bin\PinTop.exe') --export-icon (Join-Path $root 'assets\pintop.ico') | Out-Null
& (Join-Path $root 'build.ps1')
Write-Host "Icono regenerado: assets\pintop.ico"
