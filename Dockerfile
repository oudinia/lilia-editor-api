FROM mcr.microsoft.com/dotnet/sdk:10.0.103 AS build
WORKDIR /src

# Copy everything
COPY src/ src/
COPY Lilia.Api.slnx .

# Workaround for .NET 10 SDK bug (dotnet/msbuild#12546):
# Glob expansion for **/*.resx fails because MSBuild tries to traverse
# bin/Debug which doesn't exist when building in Release mode.
RUN mkdir -p src/Lilia.Api/bin/Debug
RUN dotnet publish src/Lilia.Api/Lilia.Api.csproj -c Release -o /app/publish

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0.3 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

RUN groupadd --system appgroup && useradd --system --gid appgroup appuser

COPY --from=build /app/publish .

RUN mkdir -p /app/uploads /app/logs && chown -R appuser:appgroup /app/uploads /app/logs

USER appuser

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Lilia.Api.dll"]
