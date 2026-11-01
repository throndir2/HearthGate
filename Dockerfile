# HearthGate server image: builds the web UI, publishes the API, runs as a non-root user.
FROM node:22-alpine AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY HearthGate.slnx ./
COPY config/ config/
COPY src/ src/
COPY --from=web /src/src/HearthGate.Server/wwwroot src/HearthGate.Server/wwwroot
RUN dotnet publish src/HearthGate.Server -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV HG_DATA=/data \
    HG_CONFIG=/data/hearthgate.yaml \
    DOTNET_gcServer=0
RUN mkdir -p /data && chown app:app /data
USER app
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "HearthGate.Server.dll"]
