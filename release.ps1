Write-Host "=== F1 Reaction Service - Release Manager ===" -ForegroundColor Cyan

# 1. Version abfragen
$version = Read-Host "Welche Version willst du releasen? (z.B. 0.1.5)"

if ([string]::IsNullOrWhiteSpace($version)) {
    Write-Host "No version provided. Abort!" -ForegroundColor Red
    exit
}

$csprojPath = "Service\F1ReactionService.csproj"

# Security check
if (-Not (Test-Path $csprojPath)) {
    Write-Host "Error: Could not finde $csprojPath." -ForegroundColor Red
    exit
}


Write-Host "Executing unit tests..." -ForegroundColor Yellow

# Execute the unit tests
dotnet test -v m

if ($LASTEXITCODE -ne 0) {
    Write-Host "Oops! Unit tests failed." -ForegroundColor Red
    Write-Host "Cancelled release. Nothing has been pushed." -ForegroundColor Red
    exit
}

Write-Host "All tests green! Releasing..." -ForegroundColor Green
# ---------------------------------------------

# update csproj file
$content = Get-Content $csprojPath -Raw

if ($content -match "<Version>.*</Version>") {
    $content = $content -replace "<Version>.*</Version>", "<Version>$version</Version>"
} else {
    $content = $content -replace "</PropertyGroup>", "  <Version>$version</Version>`r`n  </PropertyGroup>"
}

[System.IO.File]::WriteAllText("$PWD\$csprojPath", $content)

Write-Host "-> csproj has been updated to version $version." -ForegroundColor Green

# Commit, Tag, Push
Write-Host "-> Create commit and git tag v$version..." -ForegroundColor Yellow

git add $csprojPath
git commit -m "Release v$version"
git tag "v$version"
git push origin master
git push origin "v$version"

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "Success! Version v$version tagged and pusehd!" -ForegroundColor Green
Write-Host "GitHub will create a new docker image." -ForegroundColor Gray