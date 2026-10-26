FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/GpsTrainer.Shared/GpsTrainer.Shared.csproj src/GpsTrainer.Shared/
COPY src/GpsTrainer.Web/GpsTrainer.Web.csproj src/GpsTrainer.Web/
COPY src/GpsTrainer.Server/GpsTrainer.Server.csproj src/GpsTrainer.Server/
RUN dotnet restore src/GpsTrainer.Server/GpsTrainer.Server.csproj
COPY src/GpsTrainer.Shared/ src/GpsTrainer.Shared/
COPY src/GpsTrainer.Web/ src/GpsTrainer.Web/
COPY src/GpsTrainer.Server/ src/GpsTrainer.Server/
RUN dotnet publish src/GpsTrainer.Server/GpsTrainer.Server.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# Npgsql probes GSSAPI at startup. The aspnet image does not ship it, which logs a warning.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "GpsTrainer.Server.dll"]
