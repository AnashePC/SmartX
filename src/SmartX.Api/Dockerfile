FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/SmartX.Shared/SmartX.Shared.csproj", "src/SmartX.Shared/"]
COPY ["src/SmartX.Api/SmartX.Api.csproj", "src/SmartX.Api/"]
RUN dotnet restore "src/SmartX.Api/SmartX.Api.csproj"

COPY . .
WORKDIR /src/src/SmartX.Api
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SmartX.Api.dll"]
