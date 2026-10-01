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
COPY ["Bookings.Api/Bookings.Api.csproj", "Bookings.Api/"]
COPY ["Bookings.Application/Bookings.Application.csproj", "Bookings.Application/"]
COPY ["Bookings.Sql/Bookings.Sql.csproj", "Bookings.Sql/"]
COPY ["Bookings.Domain/Bookings.Domain.csproj", "Bookings.Domain/"]
COPY ["TicketMaster.Common/TicketMaster.Common.csproj", "TicketMaster.Common/"]
RUN dotnet restore "Bookings.Api/Bookings.Api.csproj"
COPY . .
WORKDIR "/src/Bookings.Api"
RUN dotnet build "./Bookings.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Bookings.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Bookings.Api.dll"]
