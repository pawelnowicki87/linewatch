#!/bin/sh
# Lets the same image point at any API: docker run -e API_BASE_URL=https://api.example.com ...
set -e
if [ -n "$API_BASE_URL" ]; then
    printf '{\n  "ApiBaseUrl": "%s"\n}\n' "$API_BASE_URL" > /usr/share/nginx/html/appsettings.json
fi
