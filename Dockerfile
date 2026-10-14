# СВЕЖАК — production Dockerfile (Render / любой хост).
# Multi-stage: собираем на SDK, запускаем на ASP.NET runtime.
# Порт задаётся env PORT (Render) — см. Program.cs (fallback 8080).

# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Сначала только csproj — чтобы кэшировать restore между изменениями кода
COPY SmartInventory.csproj ./
COPY Svezhak.Tests/Svezhak.Tests.csproj Svezhak.Tests/
RUN dotnet restore SmartInventory.csproj

# Код + публикация
COPY . .
RUN dotnet publish SmartInventory.csproj -c Release -o /app/publish --no-restore

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# ASPNETCORE_ENVIRONMENT=Production по умолчанию; порт — из env PORT.
# Не хардкодим URL — Program.cs читает PORT и добавляет http://0.0.0.0:PORT.
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_GCDynamicAdaptationMode=1

EXPOSE 8080
ENTRYPOINT ["dotnet", "SmartInventory.dll"]
