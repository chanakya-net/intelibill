# Node 24 matches the engines range in package.json (>=24 <26). The build ran on
# Node 22 before, outside the range the project declares. Pinned by digest so a
# rebuild of a given commit cannot pick up a different base.
FROM node:24-slim@sha256:6f7b03f7c2c8e2e784dcf9295400527b9b1270fd37b7e9a7285cf83b6951452d AS build
WORKDIR /app

# Install Bun
ENV BUN_INSTALL=/root/.bun
ENV PATH=$BUN_INSTALL/bin:$PATH
RUN apt-get update \
	&& apt-get install -y --no-install-recommends curl ca-certificates unzip \
	&& rm -rf /var/lib/apt/lists/* \
	&& curl -fsSL https://bun.sh/install | bash -s -- bun-v1.3.11

# Copy the package.json and bun.lock files
COPY ["src/frontend/package.json", "src/frontend/bun.lock", "./"]

# Install dependencies using Bun
RUN bun install --frozen-lockfile

# Copy the rest of the frontend source code
COPY ["src/frontend/", "./"]

# Build the Angular application for production
RUN bun run build

# Node, not Bun, runs the server. The SSR process proxies /hubs to the API, and
# proxying a WebSocket upgrade means hijacking the raw socket: under Bun that
# handshake returns nothing, so every SignalR connection fails. Verified both
# ways against the built bundle -- Node answers 101, Bun answers nothing.
# Bun stays in the build stage, where it installs and builds correctly.
FROM node:24-slim@sha256:6f7b03f7c2c8e2e784dcf9295400527b9b1270fd37b7e9a7285cf83b6951452d AS runtime
WORKDIR /app

# The Angular SSR build bundles express and the proxy into server.mjs, leaving
# only Node built-ins external, so the runtime image needs neither package.json
# nor node_modules.
COPY --from=build /app/dist /app/dist

ENV NODE_ENV=production \
    PORT=4000

EXPOSE 4000

# Shipped by the Node image; the server only reads from /app/dist.
USER node

CMD ["node", "dist/INVENTORY/server/server.mjs"]
