FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["F1ReactionService/F1ReactionService.csproj", "F1ReactionService/"]
RUN dotnet restore "F1ReactionService/F1ReactionService.csproj"

COPY . .
WORKDIR "/src/F1ReactionService"
RUN dotnet publish "F1ReactionService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "F1ReactionService.dll"]