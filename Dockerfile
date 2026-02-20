# --- STAGE 1: Build (Kompilieren) ---
# Wir nutzen das .NET 10 SDK zum Bauen
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Zuerst nur die Projektdatei kopieren, um Layer-Caching zu nutzen
COPY ["Service/F1ReactionService.csproj", "Service/"]
RUN dotnet restore "Service/F1ReactionService.csproj"

# Den Rest kopieren und das Projekt bauen
COPY . .
WORKDIR "/src/Service"
RUN dotnet publish "F1ReactionService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# --- STAGE 2: Runtime (Ausführen) ---
# Wir nutzen nur die schlanke Runtime für die Ausführung
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

# Nur die fertigen Binaries aus der Build-Stage kopieren
COPY --from=build /app/publish .

# Den Dienst starten
ENTRYPOINT ["dotnet", "F1ReactionService.dll"]