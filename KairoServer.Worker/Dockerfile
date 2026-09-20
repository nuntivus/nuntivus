# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app
RUN apt-get update \
	&& apt-get install -y --no-install-recommends wget \
	&& rm -rf /var/lib/apt/lists/*
USER $APP_UID
ENV DOTNET_EnableDiagnostics=0
ENV ASPNETCORE_URLS=http://+:8081
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["KairoServer.Worker/KairoServer.Worker.csproj", "KairoServer.Worker/"]
COPY ["KairoServer.Application/KairoServer.Application.csproj", "KairoServer.Application/"]
COPY ["KairoServer.Common/KairoServer.Common.csproj", "KairoServer.Common/"]
COPY ["KairoServer.Contracts/KairoServer.Contracts.csproj", "KairoServer.Contracts/"]
COPY ["KairoServer.Domain/KairoServer.Domain.csproj", "KairoServer.Domain/"]
COPY ["KairoServer.Infastructure/KairoServer.Infastructure.csproj", "KairoServer.Infastructure/"]
RUN dotnet restore "./KairoServer.Worker/KairoServer.Worker.csproj"
COPY . .
WORKDIR "/src/KairoServer.Worker"
RUN dotnet build "./KairoServer.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./KairoServer.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD wget -qO- http://localhost:8081/health/ready || exit 1
ENTRYPOINT ["dotnet", "KairoServer.Worker.dll"]