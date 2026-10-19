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
COPY src/SmartX.Client/nginx.conf /etc/nginx/conf.d/default.conf
COPY src/SmartX.Client/docker/40-write-appsettings.sh /docker-entrypoint.d/40-write-appsettings.sh

# Strip Windows line endings (if the repo was checked out on Windows) and make the script executable.
RUN sed -i 's/\r$//' /docker-entrypoint.d/40-write-appsettings.sh && chmod +x /docker-entrypoint.d/40-write-appsettings.sh

# Address the BROWSER uses to reach the API (host-mapped port from docker-compose.yml).
ENV API_BASE_URL=http://localhost:8080
EXPOSE 80
