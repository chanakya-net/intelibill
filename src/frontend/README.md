# INVENTORY

Angular frontend application for Intelibill built with Angular standalone APIs, PWA support, Tailwind CSS, PrimeNG, and NgRx root store architecture. The application is client-rendered.

## Development server

To start a local development server, run:

```bash
bun run start
```

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.

## Serving and PWA

There is no server-side rendering. Every route is behind an authentication guard
whose session lives in browser storage, so a server render could only produce the
login screen, and it cost a Node render per request plus a second code path that
every service had to survive.

`bun run build` emits two things:

- `dist/INVENTORY/browser` — the application, served as static files.
- `dist/INVENTORY/server/server.mjs` — a small Express server that serves those
  files, answers deep links with `index.html`, and proxies `/api` and `/hubs`
  (including WebSocket upgrades) to `API_ORIGIN`.

That proxy is why the process exists: the browser reaches the API on the origin
that served the page, which is what removes cross-origin requests and the CORS
configuration that would otherwise go with them. Run it with `bun run serve`.

The service worker is enabled in production builds, and the manifest and app
icons are under `public/`.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
bunx ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
bunx ng generate --help
```

## Building

To build the project run:

```bash
bun run build
```

This will compile your project and store the build artifacts in the `dist/` directory. By default, the production build optimizes your application for performance and speed.

## Running unit tests

To execute unit tests with the [Vitest](https://vitest.dev/) test runner, use the following command:

```bash
bun run test --watch=false
```

## State and Routing

- Feature-first route structure with lazy-loaded standalone pages.
- NgRx root store wiring in `src/app/core/state`.
- Feature state registration at route level in `src/app/app.routes.ts`.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.
