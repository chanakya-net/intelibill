# INVENTORY

Angular frontend application for Intelibill built with Angular standalone APIs, SSR + hydration, PWA support, Tailwind CSS, PrimeNG, and NgRx root store architecture.

## Development server

To start a local development server, run:

```bash
bun run start
```

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.

## SSR and PWA

This project is configured with server-side rendering and hydration by default.

- Build output includes browser + server bundles.
- Service worker is enabled in production builds.
- Manifest and app icons are under `public/`.

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
