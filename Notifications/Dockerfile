FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Central package management: restore cannot resolve a single version without the props files, and the
# referenced projects' csproj files are needed for the restore layer to cache on project changes only.
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["Notifications/Notifications.csproj", "Notifications/"]
COPY ["TicketMaster.Common/TicketMaster.Common.csproj", "TicketMaster.Common/"]
RUN dotnet restore "Notifications/Notifications.csproj"
COPY . .
WORKDIR "/src/Notifications"
RUN dotnet build "./Notifications.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Notifications.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Notifications.dll"]
