FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["TicketMaster.ApiGateway/TicketMaster.ApiGateway.csproj", "TicketMaster.ApiGateway/"]
RUN dotnet restore "TicketMaster.ApiGateway/TicketMaster.ApiGateway.csproj"
COPY . .
WORKDIR "/src/TicketMaster.ApiGateway"
RUN dotnet build "./TicketMaster.ApiGateway.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./TicketMaster.ApiGateway.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TicketMaster.ApiGateway.dll"]
