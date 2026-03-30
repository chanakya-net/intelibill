# Use Node.js 22 to build the Angular application (more compatible with Angular SSR builder's route extraction)
FROM node:22-slim AS build
WORKDIR /app

# Install Bun globally
RUN npm install -g bun@1.3.11

# Copy the package.json and bun.lock files
COPY ["src/frontend/package.json", "src/frontend/bun.lock", "./"]

# Install dependencies using Bun
RUN bun install --frozen-lockfile

# Copy the rest of the frontend source code
COPY ["src/frontend/", "./"]

# Build the Angular application for production
RUN bun run build

# Use Bun as the runtime environment for the SSR application
FROM oven/bun:latest AS runtime
WORKDIR /app

# Copy the package.json and built SSR application from the build stage
COPY ["src/frontend/package.json", "./"]
COPY --from=build /app/dist /app/dist

# Expose the default SSR port
EXPOSE 4000

# Set the environment to Production
ENV NODE_ENV=production

# Run the Angular SSR server
CMD ["bun", "run", "serve:ssr:INVENTORY"]
