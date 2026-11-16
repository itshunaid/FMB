FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["FMB.TiffinTracker.sln", "./"]
COPY ["src/FMB.Domain/FMB.Domain.csproj", "src/FMB.Domain/"]
COPY ["src/FMB.Application/FMB.Application.csproj", "src/FMB.Application/"]
COPY ["src/FMB.Infrastructure/FMB.Infrastructure.csproj", "src/FMB.Infrastructure/"]
COPY ["src/FMB.WebAPI/FMB.WebAPI.csproj", "src/FMB.WebAPI/"]
COPY ["src/FMB.WebMVC/FMB.WebMVC.csproj", "src/FMB.WebMVC/"]

RUN dotnet restore "FMB.TiffinTracker.sln"

COPY . .
WORKDIR "/src/src/FMB.WebAPI"
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "FMB.WebAPI.dll"]
