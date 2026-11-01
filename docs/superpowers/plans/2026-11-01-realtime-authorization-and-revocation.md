# Realtime authorization and revocation — implementation plan

**Goal:** Stop revoked, expired or wrong-shop sessions receiving newly authorized
realtime notifications.

**Spec:** [API security evidence §7](../../reviews/2026-10-21-market-readiness-evidence/api-security.md)
— *"Check active membership/status at connect, disconnect on membership/session
revocation and bound connection life to token expiry. Test a live connection while
removing a membership, disabling user, changing shop and expiring token; no later
group events may reach the revoked connection."*

**Base:** `7b82cdb5b` on `main`, 1 November 2026.

## Verified current behaviour

- [ProductHub.cs:18](../../../src/backend/Intelibill.Api/Hubs/ProductHub.cs) and
  [ShopUpdatesHub.cs:18](../../../src/backend/Intelibill.Api/Hubs/ShopUpdatesHub.cs)
  parse `active_shop_id` and join `shop:{id}` with no live authority read.
- [Program.cs:210](../../../src/backend/Intelibill.Api/Program.cs) maps both hubs
  with no `CloseOnAuthenticationExpiration`, so a live transport outlives `exp`.
- Both notifiers broadcast to the whole group with no per-recipient re-check.
- `LiveSessionValidator` + `SessionClaims` already exist and already gate every
  HTTP request from `OnTokenValidated`. The handshake therefore *is* validated
  once; nothing re-validates after it, and nothing bounds the transport.

## Design

**Connect.** A shared `RealtimeConnectionGate` admits a connection only after
`SessionClaims.TryRead` succeeds, an `active_shop_id` is present, the token's
`exp` is in the future, and `LiveSessionValidator.IsValidAsync` returns true.
It then writes a durable lease and records the `HubCallerContext` in a
process-local tracker. No SignalR group is joined and no client-callable join
method exists.

**Dispatch.** `IAuthorizedRealtimeDispatcher<THub>` replaces group broadcast. It
reads the shop's leases, drops expired ones, re-validates each distinct session
through `LiveSessionValidator`, and sends to the surviving connection ids only.
A lookup failure sends nothing — there is no group fallback.

**Revoke.** `RealtimeRevocationWorker` sweeps every 5 s: it deletes expired
leases and, for leases belonging to *this* process, aborts the transport whose
authority no longer validates.

### Ruling: sweep instead of a durable revocation record

The source plan proposed a `(userId, sessionId?, shopId?, scopeVersion, reason)`
revocation table to wake replicas, with a five-second poll as the fallback for a
lost signal. This implementation keeps only the poll, and makes it a sweep over
the lease registry re-validated against live authority.

*Why:* a signal has to be written at every revocation site (password change,
session revoke, revoke-all, role change, status change, shop switch, recovery
epoch bump). Each missed site is a silent hole. A sweep has no signal to lose,
so its worst case equals the source plan's accepted fallback bound, and its best
case does not depend on six call sites staying correct.

*Cost if wrong:* disconnection latency is up to 5 s rather than near-immediate.
Delivery is unaffected — the dispatcher authorizes every payload, so a still-open
revoked transport receives nothing in that window.

### Ruling: process-local tracker is for aborts only

`IHubContext` cannot abort a connection; only the `HubCallerContext` held by the
owning process can. The tracker is therefore process-local by necessity. It is
never consulted to decide *delivery* — that is the durable registry plus the
live validator — so no multi-replica correctness is claimed from it.

### Ruling: no row-level security policy on the lease table

`realtime_connection_leases` is an identity-class table, like `auth_sessions`,
`users` and `refresh_tokens`, none of which carry a policy. The sweep runs with
no session context and would read nothing under a `shop_id` predicate. Rows hold
no credential: a connection id, an instance id, a session id and version numbers.

*Cost if wrong:* a connection id for another shop becomes readable by the
runtime role, which already reads `auth_sessions` wholesale.

### Ruling: `RealtimeConnection` carries more than the source plan's fields

The source plan's record was
`(connectionId, instanceId, userId, sessionId, shopId, scopeVersion, expiresAt)`.
`LiveSessionValidator` also needs `securityVersion` and `activeShopRole`, and the
dispatcher needs `hubName` to avoid sending a product event to a shop-updates
transport. All three are added.

*Cost if wrong:* three extra columns.

## File map

**New**
| Path | Purpose |
|---|---|
| `Domain/Entities/RealtimeConnectionLease.cs` | the durable lease |
| `Application/Common/Realtime/RealtimeConnection.cs` | the binding record |
| `Application/Common/Interfaces/IRealtimeConnectionRegistry.cs` | registry port |
| `Infrastructure/Data/Configurations/RealtimeConnectionLeaseConfiguration.cs` | mapping |
| `Infrastructure/Services/Realtime/PostgresRealtimeConnectionRegistry.cs` | registry |
| `Api/Services/Realtime/RealtimeConnectionGate.cs` | connect-time authority |
| `Api/Services/Realtime/RealtimeConnectionTracker.cs` | local abort handles |
| `Api/Services/Realtime/RealtimeInstanceIdentity.cs` | process id |
| `Api/Services/Realtime/AuthorizedRealtimeDispatcher.cs` | per-recipient dispatch |
| `Api/Services/Realtime/RealtimeRevocationWorker.cs` | 5 s sweep |
| migration `AddRealtimeConnectionLeases` | schema |

**Changed**: both hubs, both notifiers, `Program.cs`, `ApplicationDbContext`,
both `DependencyInjection.cs`, both hub test classes, both notifier test classes,
both Angular SignalR services and their specs.

## Tasks

- [ ] **1 — Failing connect tests.** Add `Connect_RemovedMembership_IsAborted`
  and `Connect_RevokedSession_IsAborted` to `ProductHubTests` and
  `ShopUpdatesHubTests`; assert abort, no registry row.
  Verify: `dotnet test tests/backend/unit/Intelibill.Api.Unit.Tests --filter "ProductHubTests|ShopUpdatesHubTests"`.
  Expected: new tests fail to compile or fail; existing three per class still describe old behaviour.

- [ ] **2 — Gate, lease, expiry closure.** Entity, configuration, registry port
  and Postgres implementation, gate, tracker, instance identity; hubs delegate to
  the gate; `CloseOnAuthenticationExpiration = true` on both hub maps. Malformed
  or missing `sid`, a missing shop claim and a past `exp` never register.
  Expected: the four new tests pass; the rewritten existing tests pass.

- [ ] **3 — Migration.** `AddRealtimeConnectionLeases`, no RLS policy.
  Expected: `dotnet ef migrations add` emits one migration; build clean.

- [ ] **4 — Dispatcher.** `IAuthorizedRealtimeDispatcher<THub>` plus both
  notifiers converted. A failed registry lookup sends nothing and never falls
  back to a group send; an expired lease is not a recipient.
  Expected: notifier tests assert connection-id sends, not group sends.

- [ ] **5 — Revocation worker.** 5 s sweep: delete expired leases, abort local
  connections whose authority no longer validates. A transient store failure
  logs and retries rather than disconnecting everyone, because the dispatcher
  already refuses to deliver during an outage.
  Expected: worker unit tests cover abort-on-revoked, keep-on-valid, lease
  cleanup, and store-failure tolerance.

- [ ] **6 — Integration test.** `RealtimeAuthorizationIntegrationTests`: connect
  over a real transport, remove the membership, trigger a payload, assert zero
  deliveries and a bounded disconnect. Log connection ids and opaque session
  ids, never bearer query values.
  Expected: `dotnet test tests/backend/integration/Intelibill.Integration.Tests --filter RealtimeAuthorizationIntegrationTests` green.

- [ ] **7 — Client scope checks.** Connection-generation guard so a callback from
  a disposed socket cannot publish after a shop switch; a retry policy that stops
  reconnecting when the session is gone but keeps retrying a network outage.
  `ProductSignalRService` also gains the token/shop rebuild
  `ShopUpdatesSignalRService` already has. Keep the existing payload shop filter.
  Expected: `bun run test --run signalr` green, including the new
  `ShopSwitch_DelayedOldCallback_IsIgnored` and
  `SessionRevoked_StopsAutomaticReconnect` cases.

- [ ] **8 — Whole-suite verification and local run.** Full backend suite,
  frontend suite, `dotnet ef database update`, API up, web up, hub connect
  observed.

## Review focus

1. An old but still-signed JWT reconnecting after membership removal.
2. A live transport surviving past `exp`.
3. The revocation/dispatch race: what is already enqueued versus newly authorized.
4. A second replica, and a sweep that has not yet run.
5. A delayed callback from a disposed client socket after a shop switch.
