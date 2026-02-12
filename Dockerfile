FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files and restore (layer caching)
COPY src/Lilia.Core/Lilia.Core.csproj src/Lilia.Core/
COPY src/Lilia.Infrastructure/Lilia.Infrastructure.csproj src/Lilia.Infrastructure/
COPY src/Lilia.Import/Lilia.Import.csproj src/Lilia.Import/
COPY src/Lilia.Api/Lilia.Api.csproj src/Lilia.Api/
COPY Lilia.Api.slnx .
RUN dotnet restore

# Copy everything and publish
COPY src/ src/
RUN dotnet publish src/Lilia.Api/Lilia.Api.csproj -c Release -o /app/publish --no-restore

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser

COPY --from=build /app/publish .

RUN mkdir -p /app/uploads /app/logs && chown -R appuser:appgroup /app/uploads /app/logs

USER appuser

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Lilia.Api.dll"]
