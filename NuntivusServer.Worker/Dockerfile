# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
RUN apt-get update \
	&& apt-get install -y --no-install-recommends wget \
	&& rm -rf /var/lib/apt/lists/*
USER $APP_UID
ENV DOTNET_EnableDiagnostics=0
ENV ASPNETCORE_URLS=http://+:8081
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["NuntivusServer.Worker/NuntivusServer.Worker.csproj", "NuntivusServer.Worker/"]
COPY ["NuntivusServer.Application/NuntivusServer.Application.csproj", "NuntivusServer.Application/"]
COPY ["NuntivusServer.Common/NuntivusServer.Common.csproj", "NuntivusServer.Common/"]
COPY ["NuntivusServer.Contracts/NuntivusServer.Contracts.csproj", "NuntivusServer.Contracts/"]
COPY ["NuntivusServer.Domain/NuntivusServer.Domain.csproj", "NuntivusServer.Domain/"]
COPY ["NuntivusServer.Infastructure/NuntivusServer.Infastructure.csproj", "NuntivusServer.Infastructure/"]
COPY . .
RUN dotnet restore "./NuntivusServer.Worker/NuntivusServer.Worker.csproj" --locked-mode
WORKDIR "/src/NuntivusServer.Worker"
RUN dotnet build "./NuntivusServer.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./NuntivusServer.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY LICENSE /app/LICENSE
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD wget -qO- http://localhost:8081/health/ready || exit 1
ENTRYPOINT ["dotnet", "NuntivusServer.Worker.dll"]