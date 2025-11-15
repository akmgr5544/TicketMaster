FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 80
EXPOSE 81

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Events.Api/Events.Api.csproj", "Events.Api/"]
COPY ["Events.Application/Events.Application.csproj", "Events.Application/"]
COPY ["Events.Mongo/Events.Mongo.csproj", "Events.Mongo/"]
COPY ["Events.Domain/Events.Domain.csproj", "Events.Domain/"]
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
