FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["GymShop.Api/GymShop.Api.csproj", "GymShop.Api/"]
COPY ["GymShop.Application/GymShop.Application.csproj", "GymShop.Application/"]
COPY ["GymShop.Domain/GymShop.Domain.csproj", "GymShop.Domain/"]
COPY ["GymShop.Infrastructure/GymShop.Infrastructure.csproj", "GymShop.Infrastructure/"]
RUN dotnet restore "GymShop.Api/GymShop.Api.csproj"

COPY . .
RUN dotnet publish "GymShop.Api/GymShop.Api.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "GymShop.Api.dll"]
