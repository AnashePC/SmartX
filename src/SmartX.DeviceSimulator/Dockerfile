FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/SmartX.Shared/SmartX.Shared.csproj", "src/SmartX.Shared/"]
COPY ["src/SmartX.DeviceSimulator/SmartX.DeviceSimulator.csproj", "src/SmartX.DeviceSimulator/"]
RUN dotnet restore "src/SmartX.DeviceSimulator/SmartX.DeviceSimulator.csproj"

COPY . .
WORKDIR /src/src/SmartX.DeviceSimulator
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SmartX.DeviceSimulator.dll"]
