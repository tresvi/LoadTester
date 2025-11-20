# Script para compilar StampedeLoadTester en modo Release con Native AOT
# Máxima velocidad y optimizaciones

$ErrorActionPreference = "Stop"

Write-Host "Compilando StampedeLoadTester en modo Release con Native AOT..." -ForegroundColor Green
Write-Host ""

$projectPath = "StampedeLoadTester\StampedeLoadTester.csproj"
$runtime = "win-x64"

# Limpiar builds anteriores
Write-Host "Limpiando builds anteriores..." -ForegroundColor Yellow
dotnet clean $projectPath -c Release | Out-Null

# Restaurar paquetes
Write-Host "Restaurando paquetes..." -ForegroundColor Yellow
dotnet restore $projectPath

# Publicar con Native AOT
Write-Host "Publicando con Native AOT (esto puede tardar varios minutos)..." -ForegroundColor Yellow
dotnet publish $projectPath `
    -c Release `
    -r $runtime `
    --self-contained true `
    /p:Configuration=Release `
    /p:Optimize=true

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "✓ Compilación exitosa!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Ejecutable generado en:" -ForegroundColor Cyan
    $exePath = "StampedeLoadTester\bin\Release\net8.0\$runtime\publish\StampedeLoadTester.exe"
    if (Test-Path $exePath) {
        $fullPath = Resolve-Path $exePath
        Write-Host "  $fullPath" -ForegroundColor White
        Write-Host ""
        Write-Host "Para ejecutar:" -ForegroundColor Cyan
        Write-Host "  .\$exePath" -ForegroundColor White
    }
} else {
    Write-Host ""
    Write-Host "✗ Error en la compilación" -ForegroundColor Red
    Write-Host ""
    Write-Host "Si hay problemas con Native AOT e IBM MQ, puedes:" -ForegroundColor Yellow
    Write-Host "  1. Comentar <PublishAot>true</PublishAot> en el .csproj" -ForegroundColor Yellow
    Write-Host "  2. Usar: dotnet publish -c Release -r win-x64 --self-contained" -ForegroundColor Yellow
    exit 1
}

