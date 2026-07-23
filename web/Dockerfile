# syntax=docker/dockerfile:1.7

FROM node:22-alpine@sha256:16e22a550f3863206a3f701448c45f7912c6896a62de43add43bb9c86130c3e2 AS build
WORKDIR /app/web

COPY web/package.json web/pnpm-lock.yaml ./
RUN --mount=type=cache,target=/root/.local/share/pnpm/store \
  corepack enable \
  && corepack prepare pnpm@10.32.1 --activate \
  && pnpm config set registry https://registry.npmmirror.com \
  && pnpm config set fetch-retries 5 \
  && pnpm config set fetch-retry-factor 2 \
  && pnpm config set fetch-retry-mintimeout 10000 \
  && pnpm config set fetch-retry-maxtimeout 120000 \
  && pnpm config set network-timeout 300000 \
  && pnpm install --frozen-lockfile --store-dir /root/.local/share/pnpm/store

COPY web/ ./
COPY shared/ /app/shared/
RUN pnpm build

FROM nginxinc/nginx-unprivileged:1.28-alpine@sha256:7377697a821c131a924a7105fafbe7414db4e9fcc77a6f08f776f33f141ec3f8 AS runtime

COPY deploy/nginx/default.conf /etc/nginx/conf.d/default.conf
COPY --chown=nginx:nginx --from=build /app/web/dist /usr/share/nginx/html

USER nginx

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD wget -qO- http://127.0.0.1:8080/ >/dev/null || exit 1

CMD ["nginx", "-g", "daemon off;"]
