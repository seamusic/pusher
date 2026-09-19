FROM node:20-alpine AS frontend
WORKDIR /web
COPY message-pusher/web/package.json message-pusher/web/yarn.lock* ./
RUN yarn install --frozen-lockfile || yarn install
COPY message-pusher/web/ ./
RUN yarn build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY dotnet/ ./
COPY --from=frontend /web/build/ ./src/MessagePusher.Api/wwwroot/
RUN dotnet restore MessagePusher.sln
RUN dotnet publish src/MessagePusher.Api/MessagePusher.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:3000
ENV TZ=Asia/Shanghai
ENV Database__Provider=sqlite
ENV Database__SqlitePath=/data/message-pusher.db
EXPOSE 3000
VOLUME ["/data"]
WORKDIR /data
ENTRYPOINT ["dotnet", "/app/MessagePusher.Api.dll"]
