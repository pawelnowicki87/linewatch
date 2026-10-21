FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached until a project file changes.
COPY Directory.Build.props ./
COPY src/LineWatch.Core/LineWatch.Core.csproj src/LineWatch.Core/
COPY src/LineWatch.Api/LineWatch.Api.csproj src/LineWatch.Api/
RUN dotnet restore src/LineWatch.Api/LineWatch.Api.csproj

COPY src/LineWatch.Core/ src/LineWatch.Core/
COPY src/LineWatch.Api/ src/LineWatch.Api/
RUN dotnet publish src/LineWatch.Api/LineWatch.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "LineWatch.Api.dll"]
