FROM node:20-alpine AS build
WORKDIR /app
COPY frontend/package*.json ./
# `npm ci` requires package-lock.json. If there is no lockfile, fall back to `npm install`.
RUN if [ -f package-lock.json ]; then npm ci; else npm install --no-audit --no-fund; fi
COPY frontend/ ./
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
COPY frontend/nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80

