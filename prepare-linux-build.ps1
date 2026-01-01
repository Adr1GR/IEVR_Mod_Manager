# Script para preparar la migración a Linux
# Este script ayuda a identificar código específico de Windows que necesita cambios

Write-Host "=== IE:VR Mod Manager - Preparación para Build Linux ===" -ForegroundColor Cyan
Write-Host ""

# Verificar uso de Microsoft.Win32
Write-Host "1. Verificando uso de Microsoft.Win32 (Registry)..." -ForegroundColor Yellow
$registryFiles = Get-ChildItem -Recurse -Include *.cs | Select-String -Pattern "Microsoft\.Win32|Registry" | Select-Object -Unique Path, LineNumber, Line
if ($registryFiles) {
    Write-Host "   Archivos que usan Registry (necesitan cambios para Linux):" -ForegroundColor Red
    $registryFiles | ForEach-Object {
        Write-Host "   - $($_.Path):$($_.LineNumber)" -ForegroundColor White
    }
} else {
    Write-Host "   No se encontraron usos de Registry" -ForegroundColor Green
}
Write-Host ""

# Verificar uso de WPF
Write-Host "2. Verificando uso de WPF..." -ForegroundColor Yellow
$wpfFiles = Get-ChildItem -Recurse -Include *.cs,*.xaml | Select-String -Pattern "System\.Windows|xmlns.*wpf" | Select-Object -Unique Path
if ($wpfFiles) {
    Write-Host "   Archivos WPF encontrados (necesitan migración a Avalonia UI):" -ForegroundColor Red
    $wpfFiles | Select-Object -Unique Path | ForEach-Object {
        Write-Host "   - $($_.Path)" -ForegroundColor White
    }
} else {
    Write-Host "   No se encontraron archivos WPF" -ForegroundColor Green
}
Write-Host ""

# Verificar rutas hardcodeadas de Windows
Write-Host "3. Verificando rutas hardcodeadas de Windows..." -ForegroundColor Yellow
$hardcodedPaths = Get-ChildItem -Recurse -Include *.cs | Select-String -Pattern "C:\\|@\"".*\\" | Select-Object -Unique Path, LineNumber, Line
if ($hardcodedPaths) {
    Write-Host "   Rutas hardcodeadas encontradas (deben usar Path.Combine):" -ForegroundColor Yellow
    $hardcodedPaths | ForEach-Object {
        Write-Host "   - $($_.Path):$($_.LineNumber)" -ForegroundColor White
        Write-Host "     $($_.Line.Trim())" -ForegroundColor Gray
    }
} else {
    Write-Host "   No se encontraron rutas hardcodeadas" -ForegroundColor Green
}
Write-Host ""

# Verificar .csproj
Write-Host "4. Verificando configuración del proyecto..." -ForegroundColor Yellow
$csprojContent = Get-Content "IEVRModManager.csproj" -Raw
if ($csprojContent -match "UseWPF|net.*-windows") {
    Write-Host "   El proyecto está configurado para Windows:" -ForegroundColor Yellow
    if ($csprojContent -match "UseWPF") {
        Write-Host "   - Usa WPF (necesita migración a Avalonia UI)" -ForegroundColor Red
    }
    if ($csprojContent -match "net.*-windows") {
        Write-Host "   - Target framework específico de Windows" -ForegroundColor Red
    }
} else {
    Write-Host "   El proyecto parece estar configurado para multiplataforma" -ForegroundColor Green
}
Write-Host ""

Write-Host "=== Resumen ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Para crear una build para Linux, necesitas:" -ForegroundColor Yellow
Write-Host "1. Migrar de WPF a Avalonia UI o .NET MAUI" -ForegroundColor White
Write-Host "2. Reemplazar Microsoft.Win32.Registry con alternativas multiplataforma" -ForegroundColor White
Write-Host "3. Actualizar SteamHelper para detectar Steam en Linux" -ForegroundColor White
Write-Host "4. Actualizar detección de tema del sistema para Linux" -ForegroundColor White
Write-Host "5. Cambiar TargetFramework a net8.0 (sin -windows)" -ForegroundColor White
Write-Host ""
Write-Host "Ver MIGRATION_TO_LINUX.md para más detalles" -ForegroundColor Cyan
Write-Host ""
