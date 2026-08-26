FROM node:22.19-alpine AS build

ARG NPM_REGISTRY=https://registry.npmjs.org
WORKDIR /src
COPY apps/web/package.json apps/web/package-lock.json ./
RUN npm ci --registry=${NPM_REGISTRY}
COPY apps/web/ ./
RUN npm run build

FROM nginx:1.28-alpine
COPY deploy/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist /usr/share/nginx/html
EXPOSE 80
