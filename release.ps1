Write-Host "=== F1 Reaction Service - Release Manager ===" -ForegroundColor Cyan

# 1. Version abfragen
$version = Read-Host "Welche Version willst du releasen? (z.B. 0.1.5)"

if ([string]::IsNullOrWhiteSpace($version)) {
    Write-Host "Keine Version eingegeben. Abbruch!" -ForegroundColor Red
    exit
}

$csprojPath = "Service\F1ReactionService.csproj"

# Sicherheits-Check
if (-Not (Test-Path $csprojPath)) {
    Write-Host "Fehler: Konnte $csprojPath nicht finden." -ForegroundColor Red
    exit
}

# --- NEU: Das Sicherheitsnetz (Unit Tests) ---
Write-Host "🧪 Führe Unit Tests aus..." -ForegroundColor Yellow

# Startet die Tests (mit minimaler Ausgabe, damit die Konsole übersichtlich bleibt)
dotnet test -v m

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ OH NEIN! Die Tests sind fehlgeschlagen." -ForegroundColor Red
    Write-Host "Das Release wurde sicherheitshalber abgebrochen. Es wurde NICHTS zu GitHub gepusht." -ForegroundColor Red
    exit
}

Write-Host "✅ Alle Tests bestanden! Freigabe erteilt." -ForegroundColor Green
# ---------------------------------------------

# 2. csproj-Datei aktualisieren (kugelsicher)
$content = Get-Content $csprojPath -Raw

if ($content -match "<Version>.*</Version>") {
    $content = $content -replace "<Version>.*</Version>", "<Version>$version</Version>"
} else {
    $content = $content -replace "</PropertyGroup>", "  <Version>$version</Version>`r`n  </PropertyGroup>"
}

[System.IO.File]::WriteAllText("$PWD\$csprojPath", $content)

Write-Host "-> csproj wurde auf Version $version aktualisiert." -ForegroundColor Green

# 3. Git-Magie (Commit, Tag, Push)
Write-Host "-> Erstelle Commit und Git-Tag v$version..." -ForegroundColor Yellow

git add $csprojPath
git commit -m "Release v$version"
git tag "v$version"
git push origin master
git push origin "v$version"

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "🚀 ERFOLG! Version v$version wurde gestempelt und gepusht!" -ForegroundColor Green
Write-Host "GitHub baut jetzt im Hintergrund dein Docker-Image." -ForegroundColor Gray