# Merchant Web Patterns

Implementation patterns of the merchant Angular app with canonical file references. See [`architecture.md`](architecture.md) for folder structure, state shape, and routing flow, and [`README.md`](../README.md) for commands.

Check the referenced files for the canonical implementation before adding new code.

### 1. Standalone Components

No NgModules. Every component declares its own `imports` array.

**Convention:** Always use `standalone: true`. Import only what the component template directly uses — no barrel imports of entire feature sets.

---

### 2. Feature-First Layout

Each feature is self-contained: its own routes, state, and components under `features/<name>/`.

**Convention:** New features go in `src/frontend/intelibill/src/app/features/<feature-name>/`. Shared concerns (auth service, guards, interceptors) go in `core/`. Do not put feature-specific state in the root store.

---

### 3. NgRx State Boundaries

Root store holds global/shared state; all domain feature states are provided lazily at the shell route level and shared across child pages.

**Where it appears:**
- Root reducers wired in: `src/frontend/intelibill/src/app/app.config.ts`
- Shell-scoped feature state (shops, users, suppliers, inventory, customers, sales, expenses, bankAccounts) provided in: `src/frontend/intelibill/src/app/core/layout/shell.routes.ts`

**Convention:** Use NgRx for shared or async state. Use component-local state (`signal` or property) for simple UI-only cases. Never put transient UI state (e.g., a single dropdown open/close) in the store.

---

### 4. Interceptor Chain

Auth and loading concerns are cross-cutting and wired once in `app.config.ts`.

**Where it appears:**
- `src/frontend/intelibill/src/app/core/interceptors/` — `authInterceptor` (bearer token + 401 refresh with loop prevention), HTTP loading tracker
- Wired in: `src/frontend/intelibill/src/app/app.config.ts`

**Convention:** Add new cross-cutting HTTP concerns as interceptors, not as service wrappers. Order matters — auth interceptor must run before the loading tracker.

---

### 5. Route Guards

`authGuard` protects all shell/app routes. Public routes (`/login`, `/register`) are unguarded.

**Where it appears:**
- `src/frontend/intelibill/src/app/core/guards/`
- Applied in: `src/frontend/intelibill/src/app/app.routes.ts`

**Convention:** Apply `authGuard` at the shell route level, not on individual leaf routes.
