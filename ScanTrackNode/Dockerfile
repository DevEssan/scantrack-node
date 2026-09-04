# Multi-stage build — byggs från repo-roten:
#   docker build -f ScanTrackNode/Dockerfile -t scantrack-node .

# Stage 1: Bygg (hela SDK:n, ca 900 MB — följer inte med till produktion)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Kopiera bara projektfilen först och kör restore.
# Detta lager cachas och körs bara om när paketlistan i csproj ändras.
COPY ScanTrackNode/ScanTrackNode.csproj ScanTrackNode/
RUN dotnet restore ScanTrackNode/ScanTrackNode.csproj

# Nu resten av koden + CSV (csproj länkar in ../data/cities.csv till output).
# En kodändring ogiltigförklarar bara lagren härifrån och nedåt.
COPY ScanTrackNode/ ScanTrackNode/
COPY data/ data/
RUN dotnet publish ScanTrackNode/ScanTrackNode.csproj \
    -c Release -o /app/publish --no-restore

# Stage 2: Runtime (bara ASP.NET-runtime, ca 200 MB)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "ScanTrackNode.dll"]
