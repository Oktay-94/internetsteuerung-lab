# syntax=docker/dockerfile:1
# One Dockerfile, three targets: web (teacher app), firewall (OPNsense API simulator with nftables),
# client (simulated student PC). Build context is the repository root.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/Internetsteuerung.Web -c Release -o /out/web --nologo \
 && dotnet publish src/OpnsenseSim -c Release -o /out/sim --nologo

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS web
WORKDIR /app
COPY --from=build /out/web .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Internetsteuerung.Web.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS firewall
RUN apt-get update \
 && apt-get install -y --no-install-recommends nftables iproute2 openssl curl \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out/sim .
COPY lab/firewall/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh
EXPOSE 443
ENTRYPOINT ["/entrypoint.sh"]

FROM alpine:3.20 AS client
RUN apk add --no-cache iproute2 iputils netcat-openbsd curl
COPY lab/client/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh
ENTRYPOINT ["/entrypoint.sh"]
