# Admin web image. Same digest-pinned Node 24 base and Bun 1.3.11 as the
# merchant web image, built from the root Bun workspace so a shared package
# resolves to the same lockfile entry in both.
FROM node:24-slim@sha256:c86ecead9e55a5210ac1727de38a4ca840bf03dcaf34dc9749addbf383b1920c AS build
WORKDIR /app

ENV BUN_INSTALL=/root/.bun
ENV PATH=$BUN_INSTALL/bin:$PATH
RUN apt-get update \
	&& apt-get install -y --no-install-recommends curl ca-certificates unzip \
	&& rm -rf /var/lib/apt/lists/* \
	&& curl -fsSL https://bun.sh/install | bash -s -- bun-v1.3.11

# Every workspace manifest and the root lockfile arrive before the frozen
# install; the install is the same one the merchant image runs.
COPY package.json bun.lock ./
COPY src/frontend/intelibill/package.json src/frontend/intelibill/
COPY src/frontend/licensing/package.json src/frontend/licensing/
COPY src/frontend/shared/api/package.json src/frontend/shared/api/
COPY src/frontend/shared/ui/package.json src/frontend/shared/ui/

RUN bun install --frozen-lockfile

COPY src/frontend/shared/ src/frontend/shared/
COPY src/frontend/licensing/ src/frontend/licensing/
WORKDIR /app/src/frontend/licensing

RUN bun run build

# The runtime is server.mjs on Node built-ins: static files, client-side route
# fallback and a proxy for /api/admin only. No service worker ships here.
FROM node:24-slim@sha256:c86ecead9e55a5210ac1727de38a4ca840bf03dcaf34dc9749addbf383b1920c AS runtime
WORKDIR /app

# Node only: npm and its dependency tree are removed before the user switch.
# apt/libapt-pkg are upgraded past CVE-2026-109518 (fixed in 2.6.2), which the
# pinned base still ships; drop this once the base carries it.
RUN apt-get update \
	&& apt-get install -y --no-install-recommends --only-upgrade apt libapt-pkg6.0 \
	&& rm -rf /var/lib/apt/lists/* \
	&& rm -rf /usr/local/lib/node_modules/npm \
	&& rm -f /usr/local/bin/npm /usr/local/bin/npx

COPY --from=build /app/src/frontend/licensing/dist/ADMIN/browser /app/browser
COPY src/frontend/licensing/server.mjs /app/server.mjs

ENV NODE_ENV=production \
    PORT=4000

EXPOSE 4000

USER node

CMD ["node", "server.mjs"]
