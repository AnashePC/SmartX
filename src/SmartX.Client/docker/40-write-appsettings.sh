#!/bin/sh
# Runs automatically at container start (nginx image executes /docker-entrypoint.d/*.sh).
#
# The Blazor WebAssembly app runs in the USER'S BROWSER, so the API address it needs is the one reachable from
# the host (http://localhost:8080 with the provided docker-compose.yml), not the Docker-internal service name.
# Writing appsettings.json here means the same image works for any API address — just set API_BASE_URL.
set -e

API_BASE_URL="${API_BASE_URL:-http://localhost:8080}"

cat > /usr/share/nginx/html/appsettings.json <<JSON
{
  "SmartX": {
    "ApiBaseUrl": "${API_BASE_URL}"
  }
}
JSON

echo "SmartX client configured to call the API at ${API_BASE_URL}"
