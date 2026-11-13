FROM node:24-alpine AS build
WORKDIR /src
COPY NuntivusServer.Dashboard/package.json NuntivusServer.Dashboard/package-lock.json ./
RUN npm ci
COPY NuntivusServer.Dashboard/ ./
RUN npm run build

FROM nginx:1.28-alpine AS final
RUN apk add --no-cache jq
COPY --from=build /src/dist/ /usr/share/nginx/html/
COPY NuntivusServer.Dashboard/docker/nginx.conf /etc/nginx/nginx.conf
COPY NuntivusServer.Dashboard/docker/start.sh /usr/local/bin/nuntivus-dashboard-start.sh
COPY LICENSE /usr/share/licenses/nuntivus/LICENSE
RUN sed -i 's/\r$//' /usr/local/bin/nuntivus-dashboard-start.sh
ENV NUNTIVUS_APP_ENV=production
USER nginx
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 CMD wget -qO- http://127.0.0.1:8080/health/live || exit 1
ENTRYPOINT ["/bin/sh", "/usr/local/bin/nuntivus-dashboard-start.sh"]
CMD ["nginx", "-g", "daemon off;"]
