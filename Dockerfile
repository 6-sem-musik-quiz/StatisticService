FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so this layer is cached until a csproj changes
COPY global.json Directory.Build.props ./
COPY src/StatisticService.Api/StatisticService.Api.csproj src/StatisticService.Api/
COPY src/StatisticService.Service/StatisticService.Service.csproj src/StatisticService.Service/
COPY src/StatisticService.Repository/StatisticService.Repository.csproj src/StatisticService.Repository/
COPY src/StatisticService.Domain/StatisticService.Domain.csproj src/StatisticService.Domain/
RUN dotnet restore src/StatisticService.Api/StatisticService.Api.csproj

COPY src/ src/
RUN dotnet publish src/StatisticService.Api/StatisticService.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "StatisticService.Api.dll"]
