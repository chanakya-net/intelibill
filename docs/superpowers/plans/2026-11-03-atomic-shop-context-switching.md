# Atomic Shop-Context Switching Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every visible record, draft and mutation in a tab belongs to the user and shop that tab is currently authenticated for.

**Architecture:** Every write to the tab's session (sign-in, switch, shop creation, refresh, cross-tab message, sign-out) passes through one synchronous transition in `ShopContextService`. A change of `(userId, shopId)` bumps the tab's epoch, runs registered detach hooks (drafts are saved under the scope they were captured in, then cleared from memory), resets every NgRx feature slice, hides routed content, installs the new session and restarts the app at its root. HTTP results are checked against the epoch they were sent under, and owner-bound mutations carry their owner so they can never leave under another scope's token.

**Tech Stack:** Angular 21, NgRx, IndexedDB, Vitest (Bun). Frontend only; no API or database change.

**Spec:** Backlog 22 "Atomic shop-context switching" (pasted into the session on 2026-11-03), [market-readiness review](../../reviews/2026-10-21-market-readiness.md).

## What the spec assumed vs. what main has now

- Plan03 has landed: `POST shops/default` returns `ShopPreferenceResult` (no token) and `switch`/`create` return `AccessScopeResult` with `scopeVersion`. The "legacy default-shop AuthResult compatibility adapter" is therefore not needed; a test pins that a preference save never moves scope.
- An in-tab switch already restarts the app (`AppReloadService.restartAtAppRoot`, from `ShopsEffects.restartAfterShopSwitch$`). Shop creation, refresh-driven scope moves and cross-tab `SESSION_UPDATED` do not, and logout leaves NgRx feature state and the in-memory cart behind.
- `new-sale-page.lifecycle.service.ts` persists the in-memory cart under whatever shop is active when the effect runs, so a scope change writes shop A's rows into shop B's draft (RF1).
- `authInterceptor` attaches the token current at send time and lets every response through, so an old-shop response lands in the new scope (RF3) and a checkout intent frozen in A can be sent with B's token (RF4).
- Bulk intake already checks the shop between chunks, but its error path rewrites the draft with `pendingRows()` read after the await, which can be the next shop's rows.
- `tests/ui-audit/release/` does not exist (backlog 48 harness). Two-tab behaviour is covered by Vitest specs that drive two `AuthSessionService` instances over the fake `BroadcastChannel`; the Playwright release spec is deferred to plan 48.

## Global Constraints

- Angular web only; mobile excluded. Bun only (`bun run test`, `bun run build`).
- Server token/RLS stays authoritative; the client epoch is a stale-state guard, not authorization.
- No exception-driven business flow in new services: refusals return values; only the HTTP layer raises typed errors (`ScopeMismatchError`, `StaleScopeError`).
- Every new translation key exists in all nine locale files (`i18n-coverage.spec.ts`).
- Cart storage is versioned by user + shop; shop-only legacy drafts are never assigned to whoever signs in next.

## Review Focus

- RF1: A and B both have non-empty carts; a delayed A IndexedDB load finishing after the move to B never writes into B, and each draft restores under its own scope.
- RF2: Sign-out then another user's sign-in: no cart, NgRx feature row or customer from the first user is visible or loadable.
- RF3: A response to a request sent before the transition is ignored even though the server answered it.
- RF4: A checkout or bulk-intake chunk that is in flight when the scope moves keeps its original owner; nothing is ever sent for A with B's token; failed rows stay in A's draft.
- RF5: Cross-tab moves, shop creation and refresh-driven moves follow the same transition (content hidden, state reset, restart at root so guards re-run); a preference save does not.

---

## File Structure

| File | Responsibility |
|---|---|
| NEW `src/frontend/src/app/core/context/shop-scope.ts` | `ShopScope`, `ScopeOwner`, `ScopeChange`, `ScopeChangeReason`, `sameOwner`, `ownerOfSession` |
| NEW `src/frontend/src/app/core/context/shop-context.actions.ts` | `ShopContextActions.scopeChanged` + `scopeResetMetaReducer` (keeps `appShell`, `httpUi`, `authRegistration`) |
| NEW `src/frontend/src/app/core/context/scoped-request.ts` | `REQUEST_SCOPE` HttpContextToken, `scopedRequestContext(owner)`, `ScopeMismatchError`, `StaleScopeError`, `isScopeChangeError` |
| NEW `src/frontend/src/app/core/context/shop-context.service.ts` | Epoch/scope signal, `transition`, detach hooks, leave-readiness (`prepareToLeave`), mutation leases, restart policy |
| Modify `core/auth/auth-session.service.ts` | Single `commit(next, reason, broadcast)` write path; `applyAccessScope` refuses older `scopeVersion` and a session that changed underneath |
| Modify `core/interceptors/auth.interceptor.ts` | Refuse owner-bound requests whose owner is not current; drop/raise stale responses |
| Modify `core/state/index.ts`, `core/layout/shell.component.{ts,html,scss}` | Register meta-reducer; hide routed content while a transition restarts the app |
| Modify `features/shops/state/shops.effects.ts`, `features/shops/services/shop.service.ts` | Switch waits for `prepareToLeave`; restart moves to the coordinator |
| Modify `core/storage/sales-cart-indexeddb.service.ts`, `features/sales/services/sale-cart-state.service.ts`, `features/sales/pages/new-sale-page.lifecycle.service.ts` (+ template for save failure) | Owner-keyed drafts, detach, visible save failure |
| Modify `features/sales/services/{checkout-intent,sale}.service.ts`, `features/sales/state/sales.effects.ts` | Checkout sends carry the intent owner and hold a mutation lease |
| Modify `features/inventory/services/inventory.service.ts`, `features/inventory/pages/inventory-batch-page.component.ts` | Chunks carry the captured owner, hold a lease, and never write another scope's rows |
| Modify 9 × `public/assets/i18n/*.json` | `shell.scopeTransition`, `errors.shops.switchBlockedByPendingOperation`, `errors.shops.switchDraftSaveFailed`, `sales.newSale.draftSaveFailed` |

---

### Task 1: One transition coordinator

**Files:** create `core/context/{shop-scope,shop-context.actions,scoped-request,shop-context.service}.ts` + `shop-context.service.spec.ts`; modify `auth-session.service.ts`, `auth.interceptor.ts`, `core/state/index.ts`, `shell.component.*`, `shops.effects.ts`, `shop.service.ts`; extend `auth.service.token-flows.spec.ts`, `auth.interceptor.spec.ts`, `shops.effects.spec.ts`.

**Interfaces (Produces):**
- `interface ScopeOwner { userId: string; shopId: string }`, `interface ShopScope extends ScopeOwner { epoch: number }`
- `type ScopeChangeReason = 'sign-in' | 'scope' | 'refresh' | 'cross-tab' | 'sign-out'`
- `interface ScopeChange { previous: ShopScope | null; next: ShopScope | null; reason: ScopeChangeReason }`
- `ShopContextService`: `scope: Signal<ShopScope | null>`, `epoch(): number`, `transitioning: Signal<boolean>`, `initialize(owner: ScopeOwner | null): void`, `transition(next: ScopeOwner | null, reason, install: () => void): ScopeChange | null` (null when owner unchanged), `onDetach(hook: (change: ScopeChange) => void): () => void`, `onLeave(check: () => Promise<boolean>): () => void`, `prepareToLeave(): Promise<LeaveReadiness>` where `LeaveReadiness = { ok: true } | { ok: false; reason: 'mutation-in-flight' | 'draft-save-failed' }`, `beginMutation(): { release(): void }`, `hasMutationsInFlight(): boolean`, `isCurrent(owner): boolean`. (A generic `runScoped` helper was dropped: the interceptor's epoch check and the cart's load token cover every async result this change touches.)
- `REQUEST_SCOPE: HttpContextToken<ScopeOwner | null>`, `scopedRequestContext(owner: ScopeOwner): HttpContext`

**Restart policy:** restart at root when both `previous` and `next` are non-null and the owner changed; hide routed content (`transitioning = true`) from that moment. Sign-out resets state and leaves navigation to the existing callers (`/login`). Sign-in from no session resets nothing visible and does not restart.

- [ ] **Step 1: Failing specs** in `shop-context.service.spec.ts` and the existing auth/interceptor/effects specs:
  - `old_response_is_ignored` (RF3): request A pending → session moves to B → flush A → subscriber gets no value, a request with `REQUEST_SCOPE` gets `StaleScopeError`.
  - `owner_bound_request_is_refused_under_another_scope` (RF4): `REQUEST_SCOPE` = A while B is current → `ScopeMismatchError`, `http.expectNone`.
  - `cross_tab_scope_change_follows_transition` (RF5): second service instance broadcasts B → first tab's epoch +1, detach hook called once with `{previous: A, next: B, reason: 'cross-tab'}`, `restartAtAppRoot` called once, `transitioning()` true.
  - `default_preference_does_not_change_active_scope`: `applyShopPreference` → same epoch/token/shop, no detach, no restart.
  - `scope_result_retains_refresh_secret`: `applyAccessScope` keeps `refreshToken` and installs one transition.
  - `older_scope_version_is_refused`: current token `scope_version` 5, result `scopeVersion` 4 → session unchanged, returns false.
  - `meta_reducer_resets_feature_slices`: `scopeChanged` keeps `appShell`/`httpUi`/`authRegistration`, drops every other key.
  - `switch_waits_for_leave_readiness`: `prepareToLeave` → `{ ok: false, reason: 'mutation-in-flight' }` → `switchActiveShopFailed({ errorMessage: 'errors.shops.switchBlockedByPendingOperation' })`, no HTTP.
- [ ] **Step 2:** `bun run test --run src/app/core/context` → FAIL (module missing).
- [ ] **Step 3: Implement.** `AuthSessionService` routes constructor load (`initialize`), `setSession`, `clearSession`, broadcasts and `tokenState` writes through `commit(next, reason, broadcast)`, which calls `shopContext.transition(ownerOfSession(next), reason, install)`. `applyAccessScope(result, expected?)` returns boolean and refuses when the current session's user/sessionId differ from `expected` or `result.scopeVersion` is below the current token's `scope_version` claim. Interceptor: owner-bound check before send; after send, a non-exempt (not `/auth/*`, `shops/switch`, `shops` create) response or error from an older epoch becomes `StaleScopeError` when owner-bound, else completes empty. Shell hides `<main>` content and shows `shell.scopeTransition` while `transitioning()`. `ShopsEffects.switchActiveShop$` awaits `prepareToLeave()`; `restartAfterShopSwitch$` is removed (the coordinator restarts).
- [ ] **Step 4:** `bun run test --run src/app/core` and `--run src/app/features/shops` → PASS.
- [ ] **Step 5:** Commit `feat(shop-context): route every scope change through one transition`.

### Task 2: Draft and mutation ownership

**Files:** modify `sales-cart-indexeddb.service.ts` (+spec), `sale-cart-state.service.ts` (+spec), `new-sale-page.lifecycle.service.ts`, new-sale template/state for save failure, `checkout-intent.service.ts`, `sale.service.ts`, `sales.effects.ts` (+spec), `inventory.service.ts`, `inventory-batch-page.component.ts` (+spec).

**Interfaces:**
- Consumes: `ScopeOwner`, `ShopContextService.onDetach/onLeave/beginMutation/isCurrent`, `scopedRequestContext`.
- Produces: `SalesCartIndexedDbService.loadCart(owner, maxAgeMs)`, `loadServiceCart(owner, maxAgeMs)`, `saveCart(owner, items, serviceItems)`, `clearCart(owner)`; DB version 2, store `sales-cart-by-owner` keyed by `ownerKey = "<userId>|<shopId>"`; v1 store `sales-cart` left untouched and never read. `SaleCartStateService.owner: Signal<ScopeOwner | null>`, `loadPersistedCart(owner, retentionMs, onApplyDefaults?)`, `persistCart(): Promise<boolean>`, `detach(): Promise<boolean>`, `draftSaveFailed: Signal<boolean>`. `SaleService.recordSale(request, owner?)`, `InventoryService.addInventoryBatch(request, owner?)`.

- [ ] **Step 1: Failing specs:**
  - `a_b_nonempty_drafts_restore_independently` (RF1): owner A cart + service rows saved; move to B (B has its own rows); delayed A load resolves after → memory holds only B; reloading A returns A's rows unchanged.
  - `logout_clears_memory_not_ownership` (RF2): user U1 cart in shop S; sign-out → memory empty; user U2 loads S → empty; U1 loads S → original rows.
  - `persist_writes_under_captured_owner`: owner A, session moves to B before `persistCart` → write keyed A.
  - `failed_save_is_visible`: storage rejects → `persistCart()` false, `draftSaveFailed()` true, rows still in memory.
  - `uncertain_checkout_survives_switch` (RF4): checkout send for intent owned by A while B current → not sent, intent marked uncertain, lease released.
  - `bulk_chunk_finishes_in_original_scope` (RF4): chunk 1 response arrives after move to B → A's submission kept, A's draft not rewritten with B's rows, no second chunk sent.
- [ ] **Step 2:** run the four spec files → FAIL.
- [ ] **Step 3: Implement.** Cart state registers `onDetach` (sync: invalidate load token, snapshot + clear memory and owner, start save under the old owner) and `onLeave` (`persistCart`). Lifecycle loads with `checkoutScope()` as owner, resets transient page state (customer, discount, credit notes, preview) when the owner changes, and persists without reading the active shop. New-sale page shows `sales.newSale.draftSaveFailed` while `draftSaveFailed()`. Checkout sends pass the intent owner and hold a mutation lease until the outcome is stored. Bulk intake captures `{userId, shopId}` once, sends each chunk with that owner and a lease, and on a scope error or a move keeps the stored submission and returns without touching rows.
- [ ] **Step 4:** run the spec files → PASS.
- [ ] **Step 5:** Commit `feat(shop-context): keep drafts and mutations with the scope that owns them`.

### Task 3: Feature stores and routes

**Files:** extend `shop-context.service.spec.ts`, `shell.component.spec.ts`; i18n locale files.

- [ ] **Step 1: Specs:** `new_user_has_no_old_detail` (RF2): store with customers/sales/shops rows → sign-out transition → every feature slice back to its initial state. `weaker_role_closes_protected_route` (RF5): transition A(Owner) → B(Staff) → `restartAtAppRoot` (route not preserved) and shell content hidden before the new session is readable by the page.
- [ ] **Step 2:** Add the four i18n keys to all nine locales.
- [ ] **Step 3:** `bun run test --watch=false` (full) and `bun run build` → PASS.
- [ ] **Step 4:** Document the transition in `docs/frontend-architecture.md` § Shop Context Transitions and a line in `AGENTS.md`/`CLAUDE.md`; commit `docs(shop-context): document scope transitions`.

## Migration, rollout and rollback

Cart drafts move to a v2 owner-keyed store; v1 shop-only drafts cannot be attributed to a user and are left unread (they expire under the 5-minute cart retention anyway). Checkout intents and offline invoices are untouched. No API or DB migration. Rolling back to v1 code opens the v2 database at version 1, fails, and starts with an empty cart: it cannot restore cross-scope persistence.

## Deferred

- Playwright two-tab release spec — needs the plan-48 release harness.
- Inventory intake and purchase-order drafts are still keyed by shop only (shared by members of one shop on one device); re-keying them by user is a separate change.
