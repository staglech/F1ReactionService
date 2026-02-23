Write-Host "=== F1 Reaction Service - Release Manager ===" -ForegroundColor Cyan

# 1. Version abfragen
$version = Read-Host "Welche Version willst du releasen? (z.B. 0.1.3)"

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

# 2. csproj-Datei aktualisieren (kugelsicher)
# Wir lesen die Datei als einen einzigen Textblock ein
$content = Get-Content $csprojPath -Raw

if ($content -match "<Version>.*</Version>") {
    # Wenn der Tag schon da ist, ersetzen wir ihn
    $content = $content -replace "<Version>.*</Version>", "<Version>$version</Version>"
} else {
    # Wenn der Tag fehlt, mogeln wir ihn vor das Ende der ersten PropertyGroup
    $content = $content -replace "</PropertyGroup>", "  <Version>$version</Version>`r`n  </PropertyGroup>"
}

# Datei sauber und ohne Formatierungsfehler zurückschreiben
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
Write-Host "ERFOLG! Version v$version wurde gestempelt und gepusht!" -ForegroundColor Green