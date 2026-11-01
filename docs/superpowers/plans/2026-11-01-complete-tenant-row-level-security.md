# Complete tenant row-level security Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make tenant isolation reproducible under the restricted PostgreSQL runtime role deployment already uses, without changing what shop switching, login, invitation acceptance or shop creation do today.

**Expected benefits:** Merchants would gain a database boundary against accidental access to another shop's records, and the boundary would be exercised by the same role, interceptor and connection pool as production rather than by privileged test credentials. Because switching, login and shop administration are proved cross-shop operations, they are classified and tested as such instead of being broken by a predicate that assumes one shop per request.

**Architecture:** Classify every mapped table once, in a checked-in manifest, as tenant business data, tenant child data, global identity data or global reference data. Tenant tables get `ENABLE`/`FORCE` row-level security with explicit `USING` **and** `WITH CHECK` predicates that parse an absent context safely. Global identity tables — the ones login, switching and invitation acceptance read and write with no usable shop context — stay outside row-level security and keep their existing application authority locks. The three proved cross-shop tenant operations acquire a transaction-local shop scope rather than a bypass flag.

**Tech Stack:** ASP.NET Core 10, C# latest, ErrorOr, FluentValidation, EF Core 10 / Npgsql / PostgreSQL, xUnit + NSubstitute, Testcontainers. No Angular or Bun change: this plan alters no HTTP contract.

**Spec:** [Market-readiness review](/Users/chanakya/projects/intelibill/docs/reviews/2026-10-21-market-readiness.md) and [API security evidence](/Users/chanakya/projects/intelibill/docs/reviews/2026-10-21-market-readiness-evidence/api-security.md).

**ID / priority / dependencies:** 06; P0 — public-signup blocker. Coordinate 02 membership status, 03 identity/session contracts, 22 client-side shop state and 23 cache isolation. 48 owns the aggregate environment-parity gate; 43/44 own deployment and restore execution. This plan supplies role/policy evidence, not a claim about live database settings.

**Revision note:** this file replaces an earlier revision of plan 06 that would have broken shop switching. Three of its proposals are deliberately dropped and the reasons are recorded under *Dropped from the previous revision*.

## Global constraints

Web/API Indian retail public SaaS only; mobile excluded. Preserve Domain ← Application ← Infrastructure ← Api, repository abstractions and `IUnitOfWork`, versionless csproj references with versions in `Directory.Packages.props`, warnings-as-errors, standalone Angular components, Bun only. Apply migrations through the migration job; API startup does not apply them. This plan adds no NuGet package and changes no request or response shape. Source verified statically at HEAD `f8b163c8d` on 2026-11-01; no tests were run while writing it. Commands are future implementation checks from the repository root. Capture results without suppressing dependency-audit failures; plan 42 owns dependency remediation.

## What must not break

Each of these is cross-shop or context-free **by design**, and each would fail under a naive `shop_id = current_setting('app.active_shop_id')` predicate applied to the identity tables.

| Flow | Why it is not single-shop | Evidence |
|---|---|---|
| Switch shop | Reads every membership, then updates the **target** shop's membership row while the request's context is still the **previous** shop | AuthSessionService.RescopeAsync, UserRepository.GetByIdWithDetailsAsync |
| Shop picker | Must list every active membership and shop name, or no second shop can ever be chosen | ShopRepository.GetMembershipsForUserAsync, AuthShopSelection.Describe |
| Login and refresh | No token exists yet, so **both** `app.current_user_id` and `app.active_shop_id` are empty while memberships are read and `LastUsedAt`/`IsDefault` are written | HttpCurrentSessionContext, AuthShopSelection.Resolve |
| Every authenticated request | `LiveSessionValidator` runs inside `OnTokenValidated`, before `AuthenticationMiddleware` assigns `HttpContext.User`, so its reads of `auth_sessions`, `users` and `shop_memberships` execute with empty context | Program.cs, LiveSessionValidator |
| Invitation acceptance | The actor is not yet a member of the invited shop and must lock the **inviter's** membership row in it; no user-scoped or shop-scoped predicate can admit that row, and a self-referencing policy on `shop_memberships` would recurse | AcceptShopInvitationCommandHandler, MembershipAuthorityRepository |
| Shop creation | Inserts the shop, the membership **and the Unknown system supplier — a tenant row — for a shop that is not the caller's active one**, before the rescope that would make it active | CreateShopCommandHandler |
| Owner shop administration | Authorized by a live membership re-read, explicitly "the shop need not be the caller's active one"; `bank_accounts` is read and written for a route-supplied shop id | ShopsController, UpdateShopBankDetailsCommandHandler, GetShopDetailsQueryHandler |
| Set default shop | Clears `IsDefault` across every membership the account holds, in all shops | SetDefaultShopCommandHandler |

## Verified current behavior and failure

The consolidated `20260425075353_InitialCreate` contains **no** row-level security at all, so every original business table — items, inventory, batches, stock transactions, sales, returns, customers and their ledger, suppliers and their ledger, expenses, bank accounts, invoice sequences — is unprotected. Twelve later tables do carry policies, beginning with `20260510103805_AddDiscountRules`. Two defects in those twelve: they declare `USING` only, and PostgreSQL applies `USING` to `INSERT` not at all, so a request in shop A can insert a row carrying shop B's `shop_id`; and they cast `current_setting('app.active_shop_id', true)::uuid` over a value that `PostgresSessionContextInterceptor` sets to the empty string whenever there is no active shop, which raises SQLSTATE 22P02 rather than returning no rows. `ApplicationDbContext` has no global query filter, so nothing in EF compensates.

Deployment already provisions the role split this plan depends on: `scripts/db/phase7-bootstrap.sql` gives the runtime identity `USAGE` without `CREATE`, makes the migrator the owner, and sets default privileges so later migrations stay reachable. Its own closing "STILL TO DO" names the two checks this plan delivers: that the runtime identity cannot disable row-level security, and that isolation holds under connection pooling. The gap is therefore in **evidence**, not in the deployed role model.

The tests prove nothing about any of it. `ApiWebApplicationFactory` re-registers the DbContext with `sp.GetServices<IInterceptor>()` only, while the session interceptor is registered as its concrete type in `DependencyInjection.cs`, so integration requests never set `app.current_user_id` or `app.active_shop_id` at all; and the Testcontainers user is a superuser, which bypasses row-level security outright. `ShopIsolationIntegrationTests` additionally substitutes in-memory repositories, so its name does not imply database coverage.

## Classification decided here

Forty-three mapped tables, classified exhaustively. The manifest in task 3 is the machine-checked copy of this table.

- **Tenant, direct `shop_id` (29):** `bank_accounts`, `credit_note_redemptions`, `credit_notes`, `customer_ledger_entries`, `customers`, `discount_rules`, `expense_categories`, `expenses`, `inventory`, `inventory_adjustments`, `inventory_batches`, `invoice_leases`, `invoice_sequences`, `item_barcode_sequences`, `items`, `purchase_order_receipt_sequences`, `purchase_order_receipts`, `purchase_order_sequences`, `purchase_orders`, `reconciliation_issues`, `sale_items`, `sale_return_items`, `sale_returns`, `sales`, `service_code_sequences`, `services`, `stock_transactions`, `supplier_ledger_entries`, `suppliers`. Twelve of these plus the two child tables already have a policy to replace; the other nineteen are new.
- **Tenant, parent-derived (2):** `purchase_order_lines`, `purchase_order_receipt_lines`. These are the only mapped tenant tables without their own `shop_id`, so no composite tenant foreign keys are needed anywhere else.
- **Global identity, exempt (11):** `users`, `shops`, `shop_memberships`, `shop_invitations`, `auth_sessions`, `refresh_tokens`, `password_reset_tokens`, `phone_challenges`, `email_ownership_challenges`, `identity_mail`, `identity_sms`. Exempt because every flow in *What must not break* reads or writes them with no usable context, and because the four shop-identity tables are already guarded by the authority-lock order and the live per-request membership re-check. `shop_memberships` and `shop_invitations` carry a `shop_id` column and are exempt anyway — the manifest records that as an explicit decision so it cannot be mistaken for an omission.
- **Global reference / infrastructure, exempt (1 + 2):** `hsn_cache`; the distributed-cache table and `__EFMigrationsHistory`. The cache table is exempt and must stay so while it shares the process `NpgsqlDataSource`, because cache connections are opened without the EF interceptor; plan 23 owns per-tenant cache keying.

**What this boundary does and does not cover.** It catches a business query or write that forgets its `shop_id` predicate — the realistic failure across 31 tables and many handlers. It does not protect the identity cluster, and it does not protect against a compromised process that can set its own session context. Both limits are deliberate and belong in the release notes, not in a policy that breaks login.

## Design and file map

Every tenant policy takes one shape, parsing an absent or empty context to `NULL` so that a missing shop sees nothing and writes nothing instead of raising 22P02:

```sql
ALTER TABLE <t> ENABLE ROW LEVEL SECURITY;
ALTER TABLE <t> FORCE ROW LEVEL SECURITY;
CREATE POLICY <t>_shop_isolation ON <t>
    USING      (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid)
    WITH CHECK (shop_id = NULLIF(current_setting('app.active_shop_id', true), '')::uuid);
```

The two parent-derived tables keep their `EXISTS` form over the parent and gain the same `WITH CHECK`, so a line can be neither read nor attached across shops.

`ITenantWriteScope.EnterAsync(Guid shopId, CancellationToken ct)` is the one sanctioned way to act on a tenant table outside the token's shop. It executes `select set_config('app.active_shop_id', $1, true)` — **transaction-local**, so it reverts at commit or rollback and cannot reach the pooled connection — and throws if no explicit transaction is open. The shop id is always server-derived: a shop the handler just created, or one whose `Owner` membership it re-read. It is never taken from a header, a claim or a request body, and no request-controlled bypass flag is introduced. Three call sites, and no others: `CreateShopCommandHandler` (around the shop, membership and system-supplier writes), `UpdateShopBankDetailsCommandHandler`, `GetShopDetailsQueryHandler`.

Registration changes so one interceptor path serves production and tests: register `PostgresSessionContextInterceptor` as `IInterceptor` in `DependencyInjection.cs` and let the existing `AddInterceptors(sp.GetServices<IInterceptor>())` pick it up. The two interceptors hook different events (connection versus command), so order is immaterial.

**Modify:** `DependencyInjection.cs`, `DatabaseOptions.cs` (add `RequireRestrictedRuntimeRole`), `Program.cs`, the three handlers above, `PostgreSqlTestFixture.cs`, `ApiWebApplicationFactory.cs`, `scripts/db/phase7-bootstrap.sql`.

**New:** `src/backend/Intelibill.Infrastructure/Data/TenantPolicyManifest.cs`; `src/backend/Intelibill.Application/Common/Interfaces/ITenantWriteScope.cs`; `src/backend/Intelibill.Infrastructure/Data/TenantWriteScope.cs`; `src/backend/Intelibill.Infrastructure/Data/RuntimeRoleGuard.cs`; `tests/backend/integration/Intelibill.Integration.Tests/ShopSwitchingUnderRlsTests.cs`, `RestrictedRoleRlsTests.cs`, `TenantPolicyManifestTests.cs`, `PooledConnectionTenantContextTests.cs`; `docs/security/tenant-policy-matrix.md`. Migrations `HardenTenantPolicyContextParsing` then `CompleteTenantRowLevelSecurity`.

**Dropped from the previous revision.** A separate auth data source, `AuthDbContext` and `AuthDatabaseOptions`: `CreateShopCommandHandler` commits identity rows and a tenant row in one transaction, and `RescopeAsync`, `SetDefaultShop` and invitation acceptance hold identity rows under one lock order, so a second connection would either split those transactions or deadlock against them. Fixed-search-path `SECURITY DEFINER` membership-discovery routines: the raw `FOR UPDATE` lookups in `MembershipAuthorityRepository` and the `Include`-based membership loads would all have to be rewritten against them, for a boundary the exempt classification already states honestly. Composite tenant foreign keys on child tables: only two mapped tenant tables lack a `shop_id`, and both already derive ownership from a parent in the same shop.

## Ordered implementation tasks

- [ ] **Task 1 — Make the harness production-shaped.** Create a restricted `intelibill_app` role in `PostgreSqlTestFixture` (`NOSUPERUSER`, `NOBYPASSRLS`, not the table owner, granted DML and sequence usage), keep the container's default user as the migrator, expose `MigratorConnectionString` and `RuntimeConnectionString`, migrate as the migrator and serve API requests as `intelibill_app`. Register the session interceptor as `IInterceptor` so the factory stops dropping it. Add `RestrictedRoleRlsTests` with `RuntimeRole_IsNotSuperuserAndCannotBypassRls`, `RuntimeRole_DoesNotOwnTenantTables`, `RuntimeRole_CannotDisableRowLevelSecurity` and `SessionContext_IsSetOnEveryPooledConnection`. Run `dotnet test tests/backend/integration/Intelibill.Integration.Tests` and record every failure the restricted role newly exposes.

  **Benefit:** Integration requests would exercise the same role, interceptor and pool as the deployed runtime, so no later green run can be an artefact of privileged test credentials, and it closes the first of the two checks `phase7-bootstrap.sql` left open.

- [ ] **Task 2 — Pin today's switching behavior, before any policy changes.** Add `ShopSwitchingUnderRlsTests` against the restricted harness: `SwitchToSecondShop_ReissuesScopedToken`, `ListMyShops_ReturnsEveryActiveMembership`, `LoginWithTwoShops_SelectsMostRecentlyUsed`, `LoginWithoutShop_CanDiscoverOnlyOwnMemberships`, `AcceptInvitation_WhileScopedToAnotherShop_Succeeds`, `SetDefaultShop_ClearsPreviousDefaultAcrossShops`, `MemberList_ShowsCoMembersOfTheRequestedShop`, `ProtectedRequest_SucceedsWhenTokenValidationRunsWithoutContext`. Assert the HTTP results and the committed rows, not just status codes. Run `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter ShopSwitchingUnderRlsTests` and require green **before** task 5 or 6 touch any policy; the same file must stay green unchanged afterwards.

  **Benefit:** Turns "switching still works" into a gate a later policy change cannot pass silently, covering the exact flows — target-shop writes, context-free login, invitation acceptance for a shop the actor has not joined — that a shop-scoped predicate on the identity tables would break.

- [ ] **Task 3 — Make absent context safe on the policies that already exist.** Add `MissingContext_SeesNothingAndCannotWrite` for a signed-in account with no active shop against each of the twelve policied tables: expect zero rows and a refused insert, and assert the error is not SQLSTATE 22P02. Write migration `HardenTenantPolicyContextParsing` replacing every existing policy with the `NULLIF` form plus an explicit `WITH CHECK`. Re-run tasks 1 and 2's suites.

  **Benefit:** Removes a live failure mode under the restricted role deployment already uses — an empty shop context raising a cast error instead of returning nothing — and closes the unchecked-`INSERT` hole in the twelve existing policies ahead of any wider rollout.

- [ ] **Task 4 — Classify every table, in code.** Add `TenantPolicyManifest` with one entry per mapped table: classification, policy name, the predicate column or parent, required index, and for each exempt table the reason it is exempt. Add `TenantPolicyManifestTests` with `EveryMappedTable_HasExplicitSecurityClassification` (exact set equality against the EF model, so a new table fails until someone classifies it), `TenantTables_EnableAndForceRls` and `TenantTables_HaveShopIdIndex`, both read from `pg_class`/`pg_policy`/`pg_index` rather than from the manifest alone. Write `docs/security/tenant-policy-matrix.md` from the manifest.

  **Benefit:** Makes every table's security treatment reviewable and forces a decision on each future table at build time, instead of leaving isolation to whoever remembers to add a policy to the next migration.

- [ ] **Task 5 — Add the sanctioned cross-shop scope, before the policies that need it.** Add `ITenantWriteScope`/`TenantWriteScope`, wire the three call sites, and give `CreateShopCommandHandler` an explicit transaction around its shop, membership and supplier writes. Tests: `Elevation_RequiresOpenTransaction` (throws otherwise), `Elevation_RevertsAfterCommitAndRollback` (pool pinned to one connection; the next request sees its own token's shop), `CreateFirstShop_SucceedsWithRestrictedRole`, `CreateSecondShop_WhileScopedToFirst_CreatesSystemSupplier`, `OwnerAdministersNonActiveShop_ReadsAndWritesBankDetails`, `NonOwner_CannotElevateIntoAnotherShop`.

  **Benefit:** Keeps shop creation and owner shop administration working exactly as they do now once write checks exist, through a server-derived, transaction-local scope that cannot leak to a pooled connection and cannot be requested by a client.

- [ ] **Task 6 — Enforce the remaining tenant tables.** Write migration `CompleteTenantRowLevelSecurity`: enable and force row-level security with `USING` and `WITH CHECK` on the nineteen unprotected tenant tables, and add `WITH CHECK` to the two parent-derived ones. Tests: `ForeignShop_SelectUpdateDeleteDenied` (row counts and unchanged data, not just status codes), `ForeignParent_InsertRejected` with the SQLSTATE, and `UnscopedRepositoryQuery_ReturnsOnlyActiveShop` using a deliberately predicate-free query so the database is proved to catch a missing application predicate. Re-run tasks 1–5.

  **Benefit:** Extends the boundary to the tables that hold the merchant's stock, sales, ledgers and money, and proves it catches a handler that forgets its own shop filter.

- [ ] **Task 7 — Prove the pool cannot carry one shop into another request.** Add `PooledConnectionTenantContextTests` with `MaxPoolSize=1`: `PooledConnection_A_B_Anonymous_DoesNotLeak` repeated enough times to cycle the connection, plus synchronous and asynchronous opening, an explicit transaction, a cancelled request, an already-open connection, and a request that follows distributed-cache traffic on the shared data source. Bind each DbContext to one request scope and assert no code path changes the session shop mid-operation outside `ITenantWriteScope`.

  **Benefit:** Closes the second check `phase7-bootstrap.sql` left open, covering the cancellation, reopen and shared-data-source paths where a reused connection is most likely to keep the previous shop's context.

- [ ] **Task 8 — Deployment evidence and a startup guard.** Add `RuntimeRoleGuard`: at startup, when `Database:RequireRestrictedRuntimeRole` is true (default outside Development), query `pg_roles` for `current_user` and refuse to start if `rolsuper` or `rolbypassrls` is set. Extend `phase7-bootstrap.sql` with the table-level grants and the verification block for the new policies, and delete the two "STILL TO DO" items this plan closes. Run the suite against a fresh migration and against an upgraded seeded schema: `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter "RestrictedRoleRlsTests|ShopSwitchingUnderRlsTests|TenantPolicyManifestTests|PooledConnectionTenantContextTests"`, then the full `dotnet test src/backend/Intelibill.slnx`. Save catalog, grant and pool output with the release artifact.

  **Benefit:** Gives deployment evidence for both fresh and upgraded schemas and makes a mis-provisioned runtime secret fail at startup rather than silently serving traffic with the boundary switched off.

## Migration, rollout and acceptance

Before enabling policies, preflight orphan child rows and cross-shop references; quarantine and report ids for repair rather than deleting financial data. Confirm the `shop_id` index each policy needs exists first — the predicate runs on every row touched. Apply grants and migration together with least-privilege runtime secrets, and validate with canary tenants before general enablement. Order matters: tasks 1–3 are safe to ship on their own and improve the current state; task 5 must ship before task 6, or shop creation and owner bank-detail administration break. Rollback keeps isolation enabled and disables affected operations, or uses a compatible previous image; never resolve a production error by granting `BYPASSRLS`. Acceptance requires all forty-three tables classified, the fresh and upgraded catalogs matching the manifest, `ShopSwitchingUnderRlsTests` green unchanged from task 2 through task 8, and pooled A→B→anonymous evidence. A superuser-backed green run does not satisfy acceptance.

## Review focus

1. Absent context during login, refresh and token validation: `MissingContext_SeesNothingAndCannotWrite` and `ProtectedRequest_SucceedsWhenTokenValidationRunsWithoutContext` must both pass — the second fails immediately if any identity table is ever policied.
2. Writing the target shop's membership row while scoped elsewhere: `SwitchToSecondShop_ReissuesScopedToken` must assert the persisted `LastUsedAt`, not only the new token.
3. Invitation acceptance for a shop the actor has not joined: `AcceptInvitation_WhileScopedToAnotherShop_Succeeds` covers the inviter-membership lock that no user-scoped policy can admit.
4. Elevation outliving its transaction: `Elevation_RevertsAfterCommitAndRollback` on a single pooled connection, and `Elevation_RequiresOpenTransaction`.
5. A table added by a future migration with no classification: `EveryMappedTable_HasExplicitSecurityClassification` must fail until it is reviewed.
