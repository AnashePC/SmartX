FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/SmartX.Shared/SmartX.Shared.csproj", "src/SmartX.Shared/"]
COPY ["src/SmartX.Client/SmartX.Client.csproj", "src/SmartX.Client/"]
RUN dotnet restore "src/SmartX.Client/SmartX.Client.csproj"

COPY . .
WORKDIR /src/src/SmartX.Client
RUN dotnet publish -c Release -o /app/publish

FROM nginx:alpine AS final
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
EXPOSE 80
