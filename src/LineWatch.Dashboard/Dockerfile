FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/LineWatch.Core/LineWatch.Core.csproj src/LineWatch.Core/
COPY src/LineWatch.Dashboard/LineWatch.Dashboard.csproj src/LineWatch.Dashboard/
RUN dotnet restore src/LineWatch.Dashboard/LineWatch.Dashboard.csproj

COPY src/LineWatch.Core/ src/LineWatch.Core/
COPY src/LineWatch.Dashboard/ src/LineWatch.Dashboard/
RUN dotnet publish src/LineWatch.Dashboard/LineWatch.Dashboard.csproj -c Release -o /app --no-restore

# The dashboard is static files after publishing, so nginx is all it needs.
FROM nginx:1.29-alpine
COPY src/LineWatch.Dashboard/nginx.conf /etc/nginx/conf.d/default.conf
COPY --chmod=755 src/LineWatch.Dashboard/docker-entrypoint.sh /docker-entrypoint.d/40-api-base-url.sh
COPY --from=build /app/wwwroot /usr/share/nginx/html
EXPOSE 80
