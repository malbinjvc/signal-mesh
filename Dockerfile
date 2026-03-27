FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY SignalMesh.sln .
COPY src/SignalMesh/SignalMesh.fsproj src/SignalMesh/
COPY tests/SignalMesh.Tests/SignalMesh.Tests.fsproj tests/SignalMesh.Tests/
RUN dotnet restore SignalMesh.sln

COPY src/ src/
COPY tests/ tests/
RUN dotnet publish src/SignalMesh/SignalMesh.fsproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN groupadd -r appuser && useradd -r -g appuser -s /sbin/nologin appuser

COPY --from=build /app/publish .

RUN chown -R appuser:appuser /app
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "SignalMesh.dll"]
