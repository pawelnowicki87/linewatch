FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/LineWatch.Core/LineWatch.Core.csproj src/LineWatch.Core/
COPY src/LineWatch.Simulator/LineWatch.Simulator.csproj src/LineWatch.Simulator/
RUN dotnet restore src/LineWatch.Simulator/LineWatch.Simulator.csproj

COPY src/LineWatch.Core/ src/LineWatch.Core/
COPY src/LineWatch.Simulator/ src/LineWatch.Simulator/
RUN dotnet publish src/LineWatch.Simulator/LineWatch.Simulator.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 5020
USER $APP_UID
ENTRYPOINT ["dotnet", "LineWatch.Simulator.dll"]
