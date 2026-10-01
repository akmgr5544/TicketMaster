FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Central package management: restore cannot resolve a single version without the props files, and the
# referenced projects' csproj files are needed for the restore layer to cache on project changes only.
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["Events.Api/Events.Api.csproj", "Events.Api/"]
COPY ["Events.Application/Events.Application.csproj", "Events.Application/"]
COPY ["Events.Cosmos/Events.Cosmos.csproj", "Events.Cosmos/"]
COPY ["Events.Domain/Events.Domain.csproj", "Events.Domain/"]
COPY ["TicketMaster.Common/TicketMaster.Common.csproj", "TicketMaster.Common/"]
RUN dotnet restore "Events.Api/Events.Api.csproj"
COPY . .
WORKDIR "/src/Events.Api"
RUN dotnet build "./Events.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Events.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Events.Api.dll"]
