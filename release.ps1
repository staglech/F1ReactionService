Write-Host "🏎️ F1 Reaction Service - Release Manager" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# 1. Version abfragen
$version = Read-Host "Welche Version willst du releasen? (z.B. 0.1.2)"

if ([string]::IsNullOrWhiteSpace($version)) {
    Write-Host "Keine Version eingegeben. Abbruch!" -ForegroundColor Red
    exit
}

$csprojPath = "Service\F1ReactionService.csproj"

# Sicherheits-Check: Sind wir im richtigen Ordner?
if (-Not (Test-Path $csprojPath)) {
    Write-Host "Fehler: Konnte $csprojPath nicht finden." -ForegroundColor Red
    Write-Host "Bitte führe das Skript direkt aus dem Ordner aus, in dem deine .sln-Datei liegt." -ForegroundColor Yellow
    exit
}

# 2. csproj-Datei aktualisieren
$content = Get-Content $csprojPath
$content = $content -replace "<Version>.*</Version>", "<Version>$version</Version>"
Set-Content -Path $csprojPath -Value $content

Write-Host "✅ csproj wurde auf Version $version aktualisiert." -ForegroundColor Green

# 3. Git-Magie (Commit, Tag, Push)
Write-Host "📦 Erstelle Commit und Git-Tag v$version..." -ForegroundColor Yellow

git add $csprojPath
git commit -m "Release v$version"
git tag "v$version"
git push origin master
git push origin "v$version"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "🚀 BÄM! Version v$version wurde erfolgreich gestempelt und zu GitHub gepusht!" -ForegroundColor Green
Write-Host "GitHub baut jetzt im Hintergrund dein Docker-Image." -ForegroundColor Gray