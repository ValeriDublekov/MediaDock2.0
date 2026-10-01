FROM node:22-alpine AS web-build
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY server/ ./server/
RUN dotnet restore server/src/MediaDock.Api/MediaDock.Api.csproj
COPY --from=web-build /src/web/dist/ ./server/src/MediaDock.Api/wwwroot/
RUN dotnet publish server/src/MediaDock.Api/MediaDock.Api.csproj --configuration Release --no-restore --output /app/api

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=build /app/api/ ./
ARG MEDIADOCK_VERSION=local
ARG MEDIADOCK_COMMIT_SHA=unknown
ARG MEDIADOCK_COMMIT_DATE_UTC=unknown
ENV ASPNETCORE_HTTP_PORTS=8080
ENV MediaDock__Version=$MEDIADOCK_VERSION \
    MediaDock__CommitSha=$MEDIADOCK_COMMIT_SHA \
    MediaDock__CommitDateUtc=$MEDIADOCK_COMMIT_DATE_UTC
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "MediaDock.Api.dll"]
