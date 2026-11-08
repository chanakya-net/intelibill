# Backend Architecture

> High-level architecture reference. For implementation patterns (DI, Repository, ErrorOr, Options, etc.) see [`docs/architectural_patterns.md`](architectural_patterns.md). For build commands, config, and test snapshots see [`AGENTS.md`](../AGENTS.md) at repo root.

## Stack

| Concern | Choice |
|---|---|
| Platform | .NET 10, ASP.NET Core, C# latest |
| Architecture | Clean Architecture / Onion (`Domain → Application → Infrastructure → Api`) |
| Request handling | Wolverine 5.24 message bus — command/query handlers in Application |
| Validation | FluentValidation 12 — validators auto-discovered from Application assembly |
| Data access | EF Core 10 + PostgreSQL (snake_case), repository + unit-of-work abstractions |
| Error flow | ErrorOr 2.0 result pattern mapped to HTTP ProblemDetails at API boundary |
| Tenancy | Multi-shop isolation: JWT `active_shop_id` + PostgreSQL RLS session context |
| Testing | xUnit 2.9 + coverlet; integration via Testcontainers (real PostgreSQL) |

## Layer Responsibilities

| Layer | Path | Responsibility |
|---|---|---|
| Domain | `src/backend/Intelibill.Domain/` | Entities, value objects, domain events, repository interfaces — zero dependencies |
| Application | `src/backend/Intelibill.Application/` | Wolverine handlers, validators, use cases, error definitions — depends on Domain only |
| Infrastructure | `src/backend/Intelibill.Infrastructure/` | EF Core DbContext, repositories, migrations, auth/token services, `PostgresSessionContextInterceptor` for RLS |
| Api | `src/backend/Intelibill.Api/` | ASP.NET Core host, controllers, global exception middleware, DI wiring |

## Request Flow

```
HTTP Request
  → Controller
  → Wolverine (ValidationBehaviour runs FluentValidation)
  → Application Handler (returns ErrorOr<T>)
  → Infrastructure (repositories, UoW, SaveChangesAsync)
  → ErrorOr maps to ProblemDetails / HTTP response
```

For authenticated requests, `active_shop_id` is extracted from JWT and set as a PostgreSQL session variable (`app.active_shop_id`) by the `PostgresSessionContextInterceptor`, enabling RLS row filtering.

## Key Functional Areas

- **Auth**: verified email enrollment, phone registration, password reset, token refresh/revoke. There is no external identity provider: an account is created only by proving ownership of its own email address or phone number.
- **Shops**: create/update shop, list memberships, switch active shop, set default shop; `GET /api/shops/me`
- **Items**: catalog CRUD + SSE streaming endpoint (`GET /api/items/stream`)
- **Inventory**: inbound stock (single + batch), batch management, void/reassign
- **Sales**: record sale, list/detail, profit-loss report, cost tracking per batch at sale time. `POST /api/sales` replays an identical request under the caller's `(shop, actor, idempotencyKey)` and answers 409 `Sale.IdempotencyConflict` for changed content; `GET /api/sales/by-idempotency-key/{key}` is the read-only reconciliation for a checkout whose response was lost (404 `Sale.IdempotencyKeyNotFound` only means nothing committed is visible yet — re-sending the identical request is what converges)
- **Customers**: CRUD + credit ledger + payment recording (keyed by `operationKey`; see § Financial Operation Receipts)
- **Suppliers**: CRUD + purchase ledger + payment recording (keyed by `operationKey`), status tracking
- **Expenses**: record + correct + categorise
- **Bank Accounts**: CRUD per shop
- **Users**: shop user management (add/edit/roles), profile self-service, password change
- **Security**: JWT bearer, `active_shop_id` claim, RLS, 403 mapping, atomic rate limiting (`RateLimitAttribute` / `RateLimitFilter` over `IAtomicRateLimitStore`; see § Rate Limiting)

## Multi-Shop Tenancy

- `Shop`, `ShopMembership`, `ShopRole` entities in Domain
- Migration `20260425075353_InitialCreate` — consolidated schema including RLS policies on `shops` / `shop_memberships`
- `PostgresSessionContextInterceptor` sets `app.current_user_id` and `app.active_shop_id` per request
- Auth responses include `activeShopId` and accessible shop list

## Refresh Credentials

A refresh credential renews one device session (`AuthSession`) and is single-use.

- **Storage.** The client receives 64 random bytes as Base64Url. `refresh_tokens.token_hash` keeps the
  hex SHA-256 of that string (`RefreshTokenSecrets`); the secret itself is never stored or logged.
  Request, command and `AuthResult` records leave bearer credentials out of their `ToString()`.
- **Rotation** (`AuthSessionService.RotateAsync`). Look the credential up by hash, then lock
  `users` → `auth_sessions` → `refresh_tokens` (the order every security transition takes) and decide
  only on values read under those locks. The presented credential is marked `consumed_at` and exactly
  one successor is linked to it (`parent_token_id`, unique). The access token is signed before commit,
  so a signing or database failure leaves no successor.
- **Reuse.** The session is the rotation family. A consumed credential presented again — to renew or to
  sign out — ends its session and every credential it issued (`refresh-reuse`), commits that, then
  returns the generic `Auth.InvalidRefreshToken`. The warning log names session, credential and user
  ids only. Unknown, revoked and expired (`now >= ExpiresAt`) credentials are refused and end nothing.
- **Browser.** `RefreshCoordinator` serializes renewals per user across tabs (Web Locks API, bounded
  localStorage lease fallback); keyed by user because a restored tab may not know its session id. A tab
  that waited adopts the successor another tab stored for the same user and session. A refusal is final
  and is never followed by adopting stored state. The refresh request times out after 12 s (treated as
  a lost connection), and a result that arrives after the tab signed out or switched account is
  dropped rather than applied.

**Support answer — "I was signed out after a refresh."** Policy is strict: there is no grace window and
no replayable copy of a successor is kept. If a renewal reached the server but its response was lost
(network drop, closed laptop lid), the browser still holds the spent credential and the next attempt
ends that device's session; the person signs in again. The same happens if a copy of the credential is
used elsewhere — which is the point. Other devices on the account are unaffected.

**Schema rollout.** `20261101090000_HashRefreshTokensAndChainRotations` is the expand step: `token`
stays (nullable, emptied) and `is_revoked` stays in step with `revoked_at` while the previous revision
serves. Every pre-existing credential and live session is revoked, so everyone signs in once. The
contract migration (separate change) re-runs the hash-and-revoke step for rows the previous revision
wrote during the deploy window, ends the sessions that hold no hashed credential, drops `token` and its
index, and makes `token_hash` required.

## Verified Phone Credentials

Phone sign-in is off unless a deployment sets `PhoneAuth:Enabled`. The legacy
`POST /api/auth/register/phone` is closed outright: it granted a session on a number nobody had
proved, so a registrant could not sign in again once the session was gone, and anyone could reserve
someone else's number. It still answers, with `503 Auth.PhoneEnrollmentUnavailable`, so an
un-updated client gets a documented refusal rather than a 404. Accounts it created keep their data
and their sessions.

**Challenges.** `phone_challenges` holds one purpose-bound proof — `enrollment`, `login` or
`phone-change` — carrying a keyed HMAC-SHA256 of a six-digit code under `Identity:PhoneCodeKey`,
never a bare digest: a million possibilities is recoverable from a database dump in milliseconds.
The key id is derived from the key, so rotating the secret stops old rows matching instead of
comparing them against the wrong key. Default policy: five-minute lifetime, five attempts,
sixty-second resend spacing, five codes per number per hour.

**Locking.** `PhoneChallengeRepository.BeginAsync` takes the same `pg_advisory_xact_lock` the email
ownership flow takes, so a phone and an email transition on one account serialize rather than race,
and `FindForUpdateAsync` reads the challenge `FOR UPDATE`. A wrong guess costs a committed attempt;
a right one is consumed in the transaction that issues the session, which is what makes two
confirmations of one delivered code produce exactly one session. A resend supersedes its
predecessor, so which code is live is never ambiguous.

**Identity rules.** `users.is_phone_verified` backfills `false` — unknown is not verified — and
while it is false the number is contact information, never a credential. Verifying a number
increments `security_version`; clearing the number clears the proof with it, so no account claims a
verified factor for a number it no longer holds. A profile edit and shop administration cannot move
a verified number at all. A number held by an account with business history that never proved it is
refused with `Auth.RecoverySupportRequired`: carriers recycle numbers, so proving one today says
nothing about having owned that account. A number held as unproved contact information on an
account with no business history is released to whoever proves it rather than merged into it.

**Delivery.** Codes go into `identity_sms` encrypted with AES-GCM under the shared
`Identity:OutboxKey`, with the destination as associated data, and `IdentitySmsWorker` delivers them
on a bounded retry lease with no transaction held across the network call. `ISmsSender` takes a
rendered body, not a code and a template id, so no transport is in a position to log a code; local
capture, restricted to Development and Testing by `PhoneAuthOptionsValidator`, is the one place a
code is logged. See [auth-phone-setup.md](auth-phone-setup.md) for the transports, the TRAI DLT
routes and the enablement gate.

## Realtime Notifications

Two hubs carry change notices for one shop: `/hubs/products` (`ProductAdded`) and
`/hubs/shop-updates` (`ShopUpdated`). Both carry what changed, never the changed record, so a
client treats them as cache invalidations and refetches.

**Admission.** `RealtimeConnectionGate` is the single entry both hubs delegate to. A connection is
admitted only when `SessionClaims.TryRead` succeeds, the token names an active shop, its `exp` is in
the future, and `ILiveSessionAuthority` — the same `LiveSessionValidator` every HTTP request is
checked against — confirms the session, the account and the membership are live. Both hub endpoints
set `CloseOnAuthenticationExpiration`, because the handshake is the only moment SignalR looks at
authentication and a transport otherwise outlives the token that opened it. There is no
client-callable method: a connection's scope is whatever the server admitted it under.

**No groups.** A SignalR group is an address, not a permission — once joined, every later event
reaches the socket for as long as it lives. `IAuthorizedRealtimeDispatcher<THub>` resolves recipients
per payload from the lease registry and re-checks live authority for each, deduplicated per distinct
session so a device with several tabs costs one lookup. A registry or authority failure sends
nothing and never falls back to a group send.

**Leases.** `realtime_connection_leases` records each admitted transport: connection id, owning
process, hub, and the identifiers a live check needs. No credential is stored. The table is
identity-class and carries no row-level security policy: both readers — dispatch on the Wolverine
local queue, and the sweep on a background worker — run with no HttpContext and therefore an empty
`app.active_shop_id`, so a shop-scoped predicate would hide the candidate rows from the dispatcher
and the closable rows from the sweep.

**Delivery is still single-replica.** `AddSignalR()` is configured with no backplane, so
`Clients.Clients(ids)` reaches only sockets the dispatching process holds — the same limit the group
broadcast had. What the shared registry buys today is that *refusal* is correct everywhere: a
replica re-reads live authority before sending, so a revocation committed anywhere stops delivery
here. Delivery across replicas needs a backplane and is not claimed.

**Revocation.** `RealtimeRevocationWorker` sweeps every five seconds: it deletes expired leases and
closes the sockets this process holds whose authority no longer validates. It is a sweep rather than
a revocation signal, so there is no notification to lose and no call site — password change, session
revoke, revoke-all, role change, status change, shop switch, recovery-epoch bump — to keep in step.
It is not what makes delivery safe; the dispatcher is. So a store it cannot reach leaves the socket
open until the next sweep rather than disconnecting everyone, and nothing reaches that socket
meanwhile. Aborting needs the `HubCallerContext`, which only the owning process holds, so
`RealtimeConnectionTracker` is process-local by necessity and is never consulted to decide delivery.

A payload authorized a moment before a revocation commits may already be in flight and cannot be
recalled. The guarantee is the other side of that line: once the revocation is visible, no new
delivery decision comes out yes.

**Clients.** `RealtimeHubConnection` keys each connection to the token and shop it was opened with,
binds every handler to that connection's generation so a frame from a replaced socket cannot publish
after a shop switch, and stops reconnecting on a refused handshake (401/403) while keeping its
backoff for a network outage.

## Rate Limiting

**Store.** `RateLimitFilter` asks `IAtomicRateLimitStore` (Application) to check and consume every
budget a request is counted against; `PostgresRateLimitStore` (Infrastructure) keeps them in
`rate_limit_buckets` (`policy_id`, `bucket_key`, fixed `window_start`/`window_seconds`, `count`,
`blocked_until`, `expires_at`), shared by every replica. One acquisition is one Read Committed
transaction: each budget's row is upserted — which creates or locks it — in ordinal
`(policy_id, bucket_key)` order, so two requests sharing buckets cannot deadlock; then either every
row is incremented or none is. A request that will be refused is refused from a plain read first —
a count only rises within its window and a backoff only ends at its stated time — so under a
flood the requests over the limit take no row lock, write nothing and never queue behind each
other; only an admissible request, or one that must start a backoff, takes the locking path, with
`synchronous_commit` off for that transaction. A window starts at the first request after the last one ended and is
never extended by later requests; an exhausted budget with a backoff is refused until
`now + backoff` from its first refusal. `lock_timeout` and the command timeout come from
`RateLimitStore:CommandTimeoutSeconds` (2). `RateLimitBucketCleanupWorker` deletes rows whose
window and backoff have both ended, in batches, with `FOR UPDATE SKIP LOCKED`. The table is
logged (a failover must not hand out fresh allowances), is not an EF entity, and stays outside RLS:
it holds no shop data and is written before a request has a shop context.

**Budgets.** `RateBudgetFactory` builds them from what a caller cannot vary at will:

| Budget | Policy id | Key material | Default |
|---|---|---|---|
| Request | `req:{METHOD} {route template}` | signed-in user (or `anonymous`) + client address | attribute, else 100/min, 3 min backoff |
| Account (`Subject = Account`) | `subj:account` | normalized email/phone from the request model, else the signed-in user | 20 / 15 min |
| Destination (`Subject = Destination`) | `subj:destination`, `subj:destination:day` | normalized email/phone from the request model | 3 / 15 min and 10 / day |

The route template, not the raw path, so record ids do not fragment a budget. The client address is
what forwarded-header processing settled on: IPv4-mapped IPv6 counts as IPv4, other IPv6 per /64.
Emails are trimmed and lower-cased, phones normalized to E.164 with `PhoneAuth:DefaultCountryCode`,
exactly as the identity services store them. Request models opt in by implementing
`IRateLimitSubject` explicitly. Account and destination budgets count every attempt, known account
or not, and never reach headers: `X-RateLimit-*` describe the request budget only, and a refusal
answers the existing 429 body with `Retry-After` capped at a day. Sign-in, reset confirmation,
reauthentication and password change are Account endpoints; every endpoint that sends mail or SMS —
including shop invitations — is a Destination endpoint, so one mailbox shares one allowance across
registration, reset, claim, change and invitation.
Per-challenge attempts stay with the challenge (`PhoneAuth:MaxAttempts`).

**Ordering.** The filter is registered at order -3000, ahead of `ModelStateInvalidFilter`
(-2000): a body that fails validation still spends the caller's budget. A request whose body names
no subject simply has no subject budget.

**Keys.** Bucket keys are HMAC-SHA256 under `RateLimiting:BucketKey` (base64, 32 bytes, from the
secret store, shared by every replica). To rotate, move the old value to
`RateLimiting:PreviousBucketKey` and set a new `BucketKey`: every budget is counted under both
until `PreviousBucketKey` is removed, which is safe once a day (the longest window) has passed.
Without a key the material is hashed with unkeyed SHA-256 and an error is logged outside
Development/Testing: limits still hold across replicas, but a leaked table can be checked against
guessed addresses. The first key is a rotation too: set `BucketKey` with
`PreviousBucketKey=unkeyed` so existing counts carry over. A per-process random key is deliberately not the fallback — each replica would
count the same caller in a different bucket.

**Store unavailable.** Endpoints with `FailClosed = true` — every credential check and message send
— answer 503 `RateLimit.Unavailable` with `Retry-After: 5` and never reach the handler. Others fall
back to `LocalFallbackRateLimiter`: fixed windows in this process (10 000 at most; when full, an
unseen bucket is refused), applied to the request budget only, with no `X-RateLimit-*` headers so
they are never mistaken for the shared limit. Both paths count
`intelibill.rate_limit.store_unavailable{mode}`; every decision counts
`intelibill.rate_limit.decisions{outcome}`. Tags are a closed set — no route, address or subject.

**Rollout.** `RateLimiting:SubjectMode = Shadow` enforces request budgets and only counts account and
destination budgets (`outcome=shadow_denied`); switch to `Enforce` (the default) once the numbers
look right. `RelaxEndpointPolicies` (Development/Testing only) widens account and destination
budgets to the global default too. Shared NAT (offices, carrier-grade NAT) shares the request
budget; the account and destination budgets are what bound a distributed attacker, and they expire
on their own, so no target is ever locked out for longer than its window.

## Safe Errors and Diagnostics

**Public contract.** An exception that reaches `ExceptionHandlingMiddleware` is answered as
`application/problem+json` `{ type, title, status, code, traceId, errors? }` by `SafeErrorFactory`
(`Api/PublicErrors`). The exception's message, type, stack, inner exceptions and `Data` never
appear. `FluentValidation.ValidationException` → 400 `Validation.Failed` with field `errors`
(authored messages only; one that repeats the attempted value is replaced); `BadHttpRequestException`
→ its status, `Request.Invalid`; `ArgumentException` → 400 `Request.Invalid` with no text, because
a 400 does not make arbitrary text safe; `UnauthorizedAccessException` → 401 `Auth.Unauthorized`;
anything else → 500 `Internal.Unexpected`. ErrorOr results are authored text and keep their
`ToProblemResult` shape. The web falls back to its own translated key whenever `detail` is absent.

**Trace id.** `SafeTraceId.Resolve` returns the request activity's W3C trace id (the runtime accepts
only 32 hex characters from `traceparent`), else `HttpContext.TraceIdentifier` if it matches
`^[A-Za-z0-9:._-]{1,64}$`, else a fresh random id. The same value is in the response and in the
diagnostic, so an incident is found from the trace id alone.

**Started responses and streams.** If the response has already started, the middleware writes the
diagnostic and aborts the connection rather than appending a problem document. The offline snapshot
(`GET api/sales/offline-snapshot/stream`) ends a stream that fails after its first record with an
`error` record `{ snapshotId, code: "OfflineSnapshot.StreamFailed", message, traceId }` (fixed text)
and never a `complete`; a failure before the first record reaches the middleware as an ordinary 500.
The web treats the `error` record as terminal. A caller who disconnects is not an incident
(`CallerDisconnect`: the request was aborted and what surfaced is a cancellation or Kestrel's
`IOException`): the middleware answers 499 with no body and no diagnostic, request logging records
it at Information, and the snapshot writes no error record for nobody to read.

**Diagnostics.** `SafeDiagnosticWriter` logs one event per failure from allowlisted facts: error
code, status, method, route template, trace id, whether the response had started, the exception
type chain, `Type.Method` frames without file paths, and two reviewed adapters — `SqlState` from a
`DbException` and `ProviderStatus` from an `HttpRequestException`. It never hands the exception
object to the logger.

**Redaction backstop.** Every Serilog sink — console, OTLP, and any `ILogEventSink` registered in
the container — sits behind `LogEventRedactor` (`SerilogExtensions.Redacted`). Per event it drops
the exception object for the same type/frame description, masks credential-named properties
(authorization, cookie, tokens, `*password`, secret, api key, `code`, otp, `*link`, connection and
query strings…) at any depth in structures, sequences and dictionaries — and email/phone/card/
Aadhaar/PAN names and free-text email addresses when `Observability:Logging:EnablePiiMasking` is
on — scrubs `name=value` secrets, `Bearer`/`Basic` credentials and stringified exceptions from
string values and message text, and bounds depth (8), width (100) and string length (4096). It is
a backstop for structured and well-formed values; it cannot find a secret in prose, so sources
must not log one. A sink added outside `Redacted()` would receive raw events.

**Traces.** `SafeSpanProcessor` runs before the OTLP trace exporter and clears a failed span's
status description and its `exception.message`/`exception.stacktrace` tags: EF Core instrumentation
writes the provider's message (which can quote a customer's input) into the status, and Wolverine
does the same for a failed handler. The error status and exception type stay. Span events cannot
be edited; ASP.NET Core and HttpClient instrumentation add exception events only with
`RecordException`, which is off.

**Already outside the application's logs.** Request logging records the path, not the query.
`Microsoft.AspNetCore` events below Warning (which carry query strings) are filtered. OpenTelemetry
ASP.NET Core and HttpClient instrumentation redact query values by default, and HttpClient spans
whose query names an identity secret are dropped. Hub `access_token` is read on `/hubs` only.
Ingress/platform access logs are outside this boundary and must be configured not to keep query
strings.

## Financial Operation Receipts

A non-repeatable financial command carries an `operationKey` and runs through
`IFinancialOperationExecutor` (`Infrastructure/Services/Operations/FinancialOperationExecutor.cs`).
Adopted flows include customer payments (`customer-payment`), supplier payments
(`supplier-payment`) and PO receiving (`purchase-order-receipt`; see § Purchase Receipts, Price
Revisions and Cost Corrections). Sales keep their own `(shop, actor, idempotencyKey)` deduplication.

**Scope.** `operation_receipts` is unique on `(shop_id, operation_type, operation_key)` and not
on the actor, so another owner or manager of the shop reconciles the same operation instead of
posting it again. It is a tenant table with the standard `_shop_isolation` policy.

**One transaction.** The executor refuses to join an open transaction, begins one, and reserves
the receipt with `INSERT … ON CONFLICT DO NOTHING`. A concurrent uncommitted twin makes PostgreSQL
block that insert until the twin ends; no unique violation is raised, so no aborted transaction is
ever continued. When the insert reserves the row, the handler's mutation runs in the same unit of
work (it saves but never commits), the serialized response is written onto the receipt, and both
commit together. An action error or exception rolls back the reservation with the mutation.

**Replay.** When the insert affects no row, the attempt rolls back, rereads the committed receipt
and either returns its stored response verbatim — the original entry id and the balance as it was
then — or answers 409 `Operation.PayloadMismatch` when the canonical payload hash
(`FinancialOperationHasher`: target + business fields, amounts without trailing zeros) differs.

**Authorization first.** Handlers check membership, role and the target's shop before calling the
executor; `GET /api/financial-operations/{type}/{key}` (`OwnerOrManager`) checks active
membership and role before reading. A 404 `Operation.NotFound` means nothing committed is visible
yet, never that a timed-out request failed — resending the identical request is what converges.

**Compatibility.** `operationKey` is optional until every client sends one; an unkeyed payment
still posts and logs `Legacy unkeyed financial command`. Receipts are retained financial records
and are never deleted.

**Web.** `FinancialOperationIntentService` (`core/services`) writes the frozen payload and a fresh
key to `localStorage` before sending (a failed write sends nothing), replays only that payload, keeps
the intent `unknown` on 0/408/429/5xx/transport loss, discards it on any other 4xx, and clears it on
success or a found lookup. It never sends an intent from another user or shop. An unresolved intent
replaces the payment form with Check status / Retry same payment.

## Bulk Stock Intake

`POST /api/inventory/inbound/batch` keeps partial success (200, or 207 with `failed[]`), but each row
is its own unit of work: `IInboundRowExecutor` (`Infrastructure/Services/StockIntake`) opens a fresh
DI scope per row — its own `DbContext`, tracker and item/inventory caches — and commits the row's
item, batch, IN movement, inventory and payable together. A rejected row leaves no write; rows commit
in order, so a later duplicate of an item + batch answers `Inventory.BatchNumberAlreadyExists`.

**Keys.** With `operationKey` (1–60 chars) the handler first commits a `BulkIntakeManifest` receipt
whose hash covers every `clientRowId` (required, ≤ 64, unique) and row hash in order, then runs each
row under a `BulkIntakeRow` receipt keyed `{len}:{operationKey}:{clientRowId}`. Resending the same
request replays committed rows verbatim and runs the rest; any changed, added or removed row answers
409 `Operation.PayloadMismatch` before a row runs. Rejected rows are not recorded and are evaluated
again on a resend. Bulk receipts have no lookup slug: resending is the recovery path.

**Failures.** For a keyed row, an unplanned failure while the row is staged and saved (connection
loss, lost unique-index race) happens inside the executor's open transaction, so it is rolled back
and reported as `retryable: true` / `Inventory.RowRetryable`; the remaining rows still run. A failure
that may follow a commit (completing or committing the receipt, replaying it) and any failure of an
unkeyed row propagate as a 500 instead, because "nothing was saved" cannot be promised. Cancellation
is never swallowed, so a cut request returns no summary that looks complete.

**Free receipts.** A receipt whose rounded value is zero (sample, bonus stock) creates stock without a
supplier payable, in bulk and single inbound; voiding it posts no reversal, and reassigning its
supplier moves the batch alone.

**Web.** The batch page freezes the rows (with one `performedAt`) into ≤ 100-row chunks, each with its
own key, in IndexedDB (`inventory-inbound-submission`) before the first request; a failed write sends
nothing. An uncertain failure (0/408/429/5xx/transport) or a `retryable` row keeps the submission and
shows a banner with **Finish save**, which resends it unchanged; until then adding, editing, removing
or scanning rows is refused, so nothing can be posted beside or instead of the frozen rows. Saved rows
leave the draft before the submission is discarded; any definitive answer discards it. A shop switch
mid-save stops sending and leaves the submission stored under the original shop.

## Stock Corrections and Voids

**Invariant.** Per item, `sum(stock_transactions.quantity) = inventory.quantity = sum(active batch
quantities)`. Every quantity change writes exactly one signed movement in the same save as the batch
and aggregate change; metadata changes write none. `StockCorrectionInvariantTests` checks this after
counts, sales, refused legacy corrections and voids; `PurchaseCostLineageTests` checks price edits,
cost corrections, returns after a price edit and voids after a correction.

**Row versions.** `inventory_batches.xmin` and `inventory.xmin` are mapped as `Version` row-version
tokens, so every update of either row is checked against the version it was read at. A stale writer
raises `DbUpdateConcurrencyException`, which handlers turn into 409 `Inventory.BatchVersionConflict`
and which otherwise reaches the caller as 409 `Concurrency.Conflict` (never 500). `GET
inventory/batches` returns each batch's `version`.

**Counts.** `POST inventory/batches/{batchId}/count` (owner/manager) takes `expectedVersion`,
`countedQuantity` (≥ 0, two places), a required `reason` and an optional `operationKey`
(`FinancialOperationType.StockCount`). The delta is computed from the observed quantity, never sent.
A nonzero delta posts one `InventoryAdjustment` (`StockCountCorrection`, notes = reason) and one
movement (`In +d` / `Out -d`, reference = adjustment number); every count writes a `stock_counts`
row linking both, or neither for an equal count. The batch row is always written, so a confirmation
racing a sale is refused rather than recorded against moved stock. A refused count is never reapplied:
the web closes the dialog, refreshes and asks for a recount. Counts never touch the supplier payable.

**Voids.** `POST inventory/batches/{batchId}/void` (owner) reverses an unconsumed receipt: one
`Reversal(-quantity)`, the same off the aggregate and a `REVERSAL` payable entry, in one save. A
receipt that was sold from, returned to or adjusted (live adjustment, or any OUT/RET/REJ/DMG/STOL
movement other than a voided adjustment's or a legacy correction's own) answers 409
`Inventory.BatchConsumed`; remaining stock is removed by counting it to zero, which leaves the
original payable owed. Supplier returns are a separate flow.

**Legacy correction.** `PUT inventory/batches/{batchId}` no longer voids and recreates the batch. It
applies prices and lot details in place exactly like `PUT .../prices` (below) and refuses what would
move stock or money with a code naming the action that does it now: a quantity change answers 400
`Inventory.BatchQuantityEditMoved` (count), a cost change `Inventory.BatchCostEditMoved` (receipt cost
correction) and a supplier change `Inventory.BatchSupplierEditMoved` (reassign). An `expectedVersion`
older than the batch answers 409 `Inventory.BatchVersionConflict`. Adjustment quantities are stored to
three places, like stock.

## Purchase Receipts, Price Revisions and Cost Corrections

**Lot vs receipt.** `InventoryBatch.Id` is the physical lot for its whole life; no flow replaces a
batch, so sales, returns and counts always find their lot. Each receipt also writes one
`purchase_receipt_values` row (single, bulk and PO intake, all in the same save as the batch):
received quantity, supplier, source (`DIRECT_INBOUND` / `PURCHASE_ORDER` with the receipt-line link /
`LEGACY_BACKFILL`), `original_total_cost` (the rounded total the payable was posted with, so the
per-unit rounding remainder is not lost) and `effective_total_cost` (original plus corrections). Its
`xmin` is the receipt version. `GET inventory/batches` returns `receiptValueId`, `receiptStatus`,
`receiptOriginalTotalCost`, `receiptTotalCost` and `receiptVersion`.

**PO lots and exact recovery.** `POST purchase-orders/{id}/receipts` accepts repeated PO-line
IDs for distinct physical lots, optional `operationId` (UUID), and optional row `clientLineId`
(UUID). Legacy requests without these fields remain accepted. PO quantities remain positive whole
units; item quantity policy still applies. Under the PO lock, all referenced lines/items, grouped
remaining quantities and scalar values are checked before any lot is processed. A remaining quantity of 10
accepts A4+B6 and refuses A6+B6 atomically. Lot identity is shop + item + trimmed, case-sensitive
batch number; repeated product barcodes are allowed. Handler failures add whitelisted
`rowErrors` (code, field, client ID/index) to the existing problem envelope; validation paths such
as `Lines[1].Quantity` remain supported. Tax-included and purchase-tax-included stay independent.

Keyed receiving uses `FinancialOperationType.PurchaseOrderReceipt = 13`, lookup slug
`purchase-order-receipt`, and the canonical UUID as the executor key. Authorization precedes
reservation/replay; only a new callback checks PO eligibility and takes the posting-period fence
then PO locks. Receipt, two or more lots, stock movements, original receipt values, payable and
operation response commit together. The hash covers the target and ordered business rows, their
client IDs, dates and both tax flags; trimmed text, decimal scale and UTC representations normalize.
An omitted timestamp stays null in the hash and is resolved only on a new posting. Exact replay
and read-only lookup work after the PO is Received/Closed or the original month closes; changed
content under the same key answers 409 `Operation.PayloadMismatch`. A lookup 404 is unresolved.

Receipt rows preserve original quantities/costs, PO/item/batch/stock-movement links and stable client
identity through later sales and additive cost corrections. New rows without a supplied client ID
use their receipt-line ID. Migration `20261107154602_AddPurchaseReceiptClientLineIdentity` adds
only nullable `client_line_id`, backfills old rows from `id` under each shop's RLS context, and adds
a partial unique receipt/client index. Null remains accepted during mixed-version rollout and maps
to `receiptLineId` on reads. Receipt history's batch label is current metadata; this change does
not introduce a historical label snapshot or a supplier-return subsystem.

**Receiving rollout and rollback.** Run the migration job before new API pods, then deploy the
new web build. API startup does not migrate. Once keyed receipts exist, retain the additive schema,
operation records and a server capable of receipt replay/lookup during rollback. Hiding new lot or
recovery UI requires a viable existing recovery path; never remove pending browser drafts or
operation records, and never put a pre-replay API behind outstanding operations. Mobile has no new
UI and retains its legacy wire contract. No deployment is performed by this change.

**Price edits.** `PUT inventory/batches/{batchId}/prices` (owner/manager) takes a required
`expectedVersion` (missing → 400 `Inventory.PriceVersionRequired`), MRP, sale price, tax rate and mode, optional batch number/expiry/manufacturing
date and reason. It updates the batch in place (stock, cost, supplier and payable untouched), always
writes the batch row so a stale form answers 409 `Inventory.BatchVersionConflict` even when no
quantity moved, appends a `batch_price_revisions` row (old/new MRP, price, tax) when a price term
changed and publishes `InventoryBatchPricingChangedDomainEvent`. Sale snapshots never change.

**Cost corrections.** `POST inventory/receipts/{receiptValueId}/cost-corrections` (owner) takes
`expectedVersion` (receipt version; missing → 400 `PurchaseReceipt.VersionRequired`), `revisedTotalCost` (0–99,999,999.99, two places), a required
`reason` and an optional `operationKey` (`FinancialOperationType.ReceiptCostCorrection`). It posts
`delta = revised − effective` as one `COST_ADJUSTED` supplier ledger entry (batch-linked, signed) and
one `purchase_receipt_cost_adjustments` row (prior, revised, delta, reason, actor, ledger link) with
the new effective total, in one save. Buy 10 at ₹100, sell 4, revise to ₹1100: payable +₹100, six
left. The batch's unit cost and every sale's COGS stay as they are; how the delta is valued
(inventory vs period variance) is an accounting decision not made here. Stale version or a lost save
race answers 409 `PurchaseReceipt.VersionConflict`; an unchanged total 400 `PurchaseReceipt.CostUnchanged`.
A correction writes the batch row and a void writes the receipt row, so a correction racing a void
of the same batch fails one of the two saves: a voided receipt can never keep a correction's delta.

**Voids and reassignment.** A void reverses the receipt's effective total to the receipt's supplier
(falling back to `original quantity × unit cost` and the batch supplier only for batches without a
resolved receipt value), so a corrected, unconsumed receipt nets to zero. Reassigning a batch from the
placeholder supplier moves the goods-received entry, every cost-adjustment entry and the receipt value
together; a voided batch cannot be reassigned, and a lost race answers 409 `Inventory.ConcurrentChange`.

**Legacy evidence.** The migration backfills receipt values for active batches that have none;
`scripts/db/backfill-purchase-receipt-values.sql` is the same idempotent statement, to re-run as the
migrator role once every API instance runs the new version (an old instance can create batches
between the migration job and the rollout). A receipt is
`RESOLVED` only with an opening `IN` movement of the full received quantity that is not a legacy
`Batch Correction`, at most one goods-received payable, and agreement with its PO line when present.
Everything else is `LEGACY_UNRESOLVED` with null amounts — never estimated from remaining stock — and
a correction answers 400 `PurchaseReceipt.EvidenceMissing`. Sales from batches the old PUT already
replaced still point at the voided batch and cannot be restocked; choosing a destination lot for
those returns is a pending policy decision. Rollback: disable correction writes; never drop the
append-only rows or restore destructive replacement.

## Sale Return Serialization

**Invariant.** Per sale line, the active (unvoided) returned quantity never exceeds the sold quantity,
and each return is voided at most once — whether the line is goods, a service or wastage, so whether
or not any stock row is written. A credit note is never both voided and redeemed.

**Lock.** Recording a return and voiding one run through `ISaleReturnMutationCoordinator`: one
transaction that, after the period fence, first takes the sale's customer row (when the sale has one) and then the sale row,
each with `SELECT … FOR NO KEY UPDATE` scoped by `shop_id` (`ISaleRepository.LockForReturnMutationAsync`),
then validates and writes, and commits only on success. The customer lock is there because a due
reduction is capped by the customer's balance across all their sales, so returns on two sales of one
customer must queue too. Validation runs under the lock, so a competing return that committed first is counted and
the loser answers 400 `SaleReturn.QuantityExceedsRemaining` (or `SaleReturn.AlreadyVoided`); the
preview stays advisory. `NO KEY UPDATE` keeps inserts that only reference the sale (their FK key-share
check) from queueing behind it. The void reads only the return's immutable sale id before the lock
(`ISaleReturnRepository.GetSaleIdAsync`) and everything it decides on after.

**Order.** (period fence, taken first) → customer → sale → that sale's credit notes (`GetBySaleReturnIdForUpdateWithRedemptionsAsync`) →
inventory batch and aggregate rows (locked by their `UPDATE` at save, `xmin`-checked). Redemption at
checkout locks credit notes and then batches and never waits on the parent sale; `VoidCreditNote`
locks only its note. Nothing locks a return or credit note and then waits on its sale.

**Stale joins.** A `FOR UPDATE` that waited returns the locked row as the holder left it but evaluates
anything joined in the same statement against the snapshot it started with, so redemptions the holder
committed are invisible to it. The credit-note lock therefore runs as its own statement before the
load, and `CreditNote.HasBeenRedeemed` also treats `AvailableBalance < OriginalAmount` as redeemed.

**Failures.** Every lock wait in the transaction is bounded at 5 s (`SET LOCAL lock_timeout`); a
timeout on any of them answers 409 `SaleReturn.SaleBusy`. A batch changed underneath by a sale or count
(`xmin`), a deadlock (40P01) or a serialization failure (40001) answers 409 `SaleReturn.ConcurrentChange`. Both roll back everything and are safe to retry after a refresh; the
client never reposts on its own. `SaleReturnConcurrencyTests` races each pair on real PostgreSQL with
a save barrier rather than sleeps. Customer payments and their allocations and reversals take the same
customer → sale locks (see § Payment Receipts and Allocations), so a payment and a due-reducing return
queue behind each other. Returns carry no
operation key yet, so a resend after a lost response is a new return and is bounded only by
entitlement.

## Payment Receipts and Allocations

**Facts.** Every customer collection and supplier payment posts, in one transaction, its ledger entry
(`PAYMENT_RECEIVED` / `PAYMENT_MADE`, plus the supplier payment's linked expense) and one immutable
`PaymentReceipt` (`payment_receipts`: party type and id, amount, `ReceiptTenderMethod`, optional bank
account and external reference, business date, ledger entry, expense). `PaymentAllocation` rows
(`payment_allocations`) say which customer invoice (`sales.id`) or supplier purchase receipt
(`purchase_receipt_values.id`) it settled; whatever is not allocated stays visible as unallocated
credit and is never attached to an invoice on its own. A UPI/card reference is cashier evidence only.

**Projection.** `IInvoiceBalanceReader` is the one balance the account UI, the allocation rules and the
return cap read. Invoice outstanding = `Sale.DueAmount` (issue-time, never rewritten) − active returns'
`DueReductionAmount` − active allocations, floored at zero; purchase receipt outstanding = effective
payable of a resolved, unvoided receipt − active allocations. An allocation is active while its receipt
has no reversal. The account ledger balance equals Σ invoice outstanding − unallocated credit
(`PaymentAllocationTests.AssertBalancesReconcileAsync`).

**Commands.** `POST customers/{id}/payments` and `suppliers/{id}/payments` take optional `method`,
`bankAccountId`, `externalReference` and `allocations`; an omitted method is `UnknownRecordedMethod`,
never cash. A request without those fields hashes exactly as before, so a pre-release retry still
replays. `POST payments/{id}/allocations` (owner/manager, optional `operationKey`) applies more of a
receipt; `POST payments/{id}/reversal` (owner only, reason 3–255, required `operationKey`) reverses the
whole receipt once: a linked `PAYMENT_REVERSAL` (customer) or `REVERSAL` (supplier) ledger entry puts
the amount back, the supplier expense is voided, the allocations stop counting, the receipt row is never
edited, and `payment_reversals.receipt_id` is unique. `GET payments/{id}` and `GET payments/balances`
serve the printable detail and the open documents.

**Locks.** `IPaymentTransactionRunner` runs keyed requests inside `IFinancialOperationExecutor` (replay
serialization first) and unkeyed ones in a plain transaction; the action then takes the party row and
its target rows sorted by id, `FOR NO KEY UPDATE` with a 5 s budget
(`IPaymentReceiptRepository.LockPartyAndTargetsAsync`), the same customer → sale order a return uses.
A lock timeout or deadlock answers 409 `Payment.Busy`. Allocation rules read outstanding under the lock
(`IPaymentAllocationPlanner`): above outstanding → 409 `Payment.AllocationExceedsOutstanding`, above the
receipt's unallocated amount → 409 `Payment.AllocationExceedsReceipt`, another party's or shop's
document → 404 `Payment.AllocationTargetNotFound`.

**Guards.** A return's due reduction is capped by `min(account balance, invoice outstanding)`, so a
return on an invoice payments already settled pays out instead of cutting another invoice's debt. A
purchase receipt with active allocations refuses void and supplier reassignment (409
`Payment.DocumentHasAllocations`) and a cost correction below the allocated amount (409
`Payment.CorrectionBelowAllocated`); those checks read before the write without the payment lock.

**Legacy.** The `AddPaymentReceiptsAndAllocations` migration gives every existing ledger payment a
receipt (`is_legacy`, `UNKNOWN_RECORDED_METHOD`, linked expense for suppliers, no allocation), shop by
shop because the migrator is not BYPASSRLS. Re-run `scripts/db/backfill-payment-receipts.sql` once after
rollout for payments an older instance recorded during the deploy. Rollback = stop calling allocation and
reversal; receipt facts stay.

## Customer Credit Policy

**Settings.** Every customer has a `CreditPolicyMode` (`customers.credit_policy_mode`): `NoCredit` (no
new due), `Enforced` (new due refused once the account would exceed `credit_limit`; needs a limit above
zero, so 0 never means "unlimited") or `LegacyAdvisory` (recorded before policies existed: the limit is
shown, labelled, never enforced). The `AddCustomerCreditPolicyAndSaleDueDates` migration sets every
existing customer to `LegacyAdvisory`; an API create without a mode is `LegacyAdvisory` too (an
ambiguous request stays visibly unenforced), an edit without one keeps the stored mode and terms. The
web app always sends an explicit mode and offers `LegacyAdvisory` only to keep it. Optional
`credit_terms_days` (0–365). Only owners and managers may set or change limit, mode or terms, or
reactivate a customer whose policy allows credit (`Customer.CreditSettingsChangeNotPermitted`, 403); a
staff-created customer starts at `NoCredit`, and the web overlays show those fields read-only to staff.

**Decision.** `CustomerCreditPolicy.Evaluate(state, exposure, newDue, override, role)` is pure. Exposure is
the signed ledger balance (plan 25 model: unapplied payments and reversals counted once; a customer in
credit has negative exposure). No new due → allowed. Inactive → 409 `Customer.CreditCustomerInactive`;
`NoCredit` → 409 `Customer.CreditNotExtended` (neither can be overridden). `Enforced` over the limit →
409 `Customer.CreditLimitExceeded` unless an override `{reason, observedExposure}` from an owner or
manager approves it: staff → 403 `Customer.CreditOverrideNotPermitted`; exposure grown past what the
approver saw → 409 `Customer.CreditOverrideStale`. `LegacyAdvisory` posts with a sale warning when it
exceeds a positive limit.

**Serialization.** `RecordSale` takes the customer row (`ICustomerRepository.LockForCreditAsync`,
`FOR NO KEY UPDATE`, 5 s budget that is reset with `SET LOCAL lock_timeout TO DEFAULT` once granted) as
the first business lock (after the operation receipt and the period fence) of the posting transaction whenever the sale adds due, then re-reads the customer
(`GetCurrentAsync`, untracked) and the balance and decides. Order: customer → invoice sequence → credit
notes → batches, consistent with payments and returns (customer first among business locks). A customer edit is an `UPDATE`
that conflicts with the same lock. Timeout → 409 `Customer.CreditBusy`. A retry whose sale committed
while it waited replays instead of being judged again; the override is part of the idempotency hash
(omitted when absent, so old hashes hold). The approval is audited on the sale
(`credit_override_reason`, `_by_user_id`, `_observed_exposure` = what the approver saw, `_limit`).
The checkout's credit panel fetches its preview again after any `Customer.Credit*` refusal and drops a
stale approval. `GET customers/{id}/credit`
(any member) is the checkout's advisory preview; it can be stale by the time the sale posts.

**Due dates.** `Sale.DueDate` = IST business date of issue + the customer's terms, fixed at issue; null
without terms or due and for every historical sale (never invented). Overdue ⇔ current outstanding > 0
and due date before the as-of business date; without a due date the debt is outstanding, never
overdue. `IInvoiceBalanceReader` returns the due date per invoice.

**Statement.** `GET customers/{id}/account?from=&to=` adds `creditLimit` (saved, exactly), `isActive`,
`creditPolicyMode`, `creditTermsDays`, `availableCredit` (null when advisory), `openingBalance` (signed
entries before `from`), `closingBalance` (opening + window entries), `asOfBusinessDate`, `overdueAmount`
(capped at what the account owes, since unallocated payments still pay it down; `from > to` → 400
`Customer.StatementWindowInvalid`);
sales add `dueDate`/`isOverdue`, entries `signedAmount`. Entries order by date, created-at, id.

**Offline.** A bill issued offline is never refused for credit: the poster evaluates the same policy
without a lock and, on a violation, posts with a warning and a `CustomerVariance` reconciliation issue
(`offline_sync.credit_policy_violation`). A reviewer posting a held sale (`CreditReviewed`) files no
new credit case. Offline devices cannot see other devices' due, so the limit is
not a cross-device guarantee; per-device credit budgets are not built. The due date uses the terms known
at sync. Ledger entry dates are still UTC dates while windows and due dates are IST business dates, so an
entry between 00:00 and 05:30 IST falls on the previous statement day (pre-existing ledger convention).

## Return Accounting Exports

**Snapshot.** Recording a return posts its accounting in the same save
(`SaleReturn.PostAccounting(ReturnAccountingSnapshot)`): per line `adjustment_taxable_amount`,
`adjustment_tax_amount` and `adjustment_non_tax_amount` (summing to the approved refund), their totals
on the return, `accounting_status` (`BALANCED` / `NEEDS_REVIEW` with `accounting_review_reason`), the
policy version, and one `sale_return_settlements` row per leg, in order: `RECEIVABLE_REDUCTION` (the
due reduction, with the customer's `RETURN_CREDIT` ledger entry; none for a walk-in sale),
`STORE_CREDIT_ISSUED` (the credit note issued) or `EXTERNAL_REFUND` (a refund paid outside the books;
the tender is not recorded, so it is never guessed). The legs always sum to the approved refund. The
original returned value (Σ `max_refund_amount`) is kept beside the financial adjustment.

**Policy.** `IReturnAccountingPolicy` (`proportional-line-tax/1`): within = min(approved, paid share);
tax = round(within × share tax / share gross, 2, away from zero); taxable = within − tax; anything
approved above the paid share is a non-tax adjustment. A line with an approved refund but no paid
share, or a share whose tax is outside its gross, answers 400 `SaleReturn.AccountingPolicyUnsupported`
(the preview warns `sale_return.accounting_unsupported`). Repeated returns allocate each component of
the invoice share cumulatively — `round(original × cumulative qty / sold qty) − already allocated` —
so the final return takes the last paisa and no component is exhausted twice.

**Posting periods.** `ReturnAdjustmentReader` loads every return processed or voided in the period
(`ISaleReturnRepository.GetPostedInRangeAsync`, with sales outside the period loaded by id) and
`ReturnAdjustmentProjector` turns it into dated entries: the return on its UTC processing date, a void
as a negated `VoidReversal` on its UTC void date. A June return voided in July shows its credit in
June and only the reversal in July. Sales XLSX, PDF, Tally and the P&L all read these entries, so
their return totals agree; export metadata carries `AccountingExportVersion` 2.

**Vouchers.** Tally builds `AccountingVoucher`s first and writes XML only from them. A return is a
`Credit Note` voucher numbered with the return number and referencing the invoice: Sales, Output GST
per rate and Sales Return Adjustments debited, each leg credited to the customer (or Walk-in
Customer), Credit Note Liability or Customer Refunds Clearing. A void is a `Journal` voucher
`{return number}-VOID`, sides swapped, referencing the credit note. A zero refund posts no voucher
(the stock fact stays in XLSX/PDF). `IAccountingVoucherValidator` checks every voucher (≥ 2 lines,
named ledgers, positive two-place amounts, debits = credits); a return needing review or an invalid
voucher refuses the whole Tally export with 409 `Export.ReturnNeedsAccountingReview`,
`Export.VoucherUnbalanced` or `Export.VoucherInvalid`, every diagnostic listed. XLSX adds a Return
Adjustments sheet (settlement split, status, reason) and PDF a return adjustments table; both still
render returns needing review, flagged. P&L return rows use the snapshot's tax split, and
`isReversal` / `needsAccountingReview` mark voids and unreviewed returns. A Credit Note voucher's
reference date is the invoice date; a reversal's is the return date. The P&L reduces revenue by the
whole refund, while Tally books a non-tax excess to Sales Return Adjustments: net profit agrees,
revenue lines differ by that excess.

**Legacy evidence.** The migration backfills every return without a snapshot;
`scripts/db/backfill-sale-return-accounting.sql` is the same idempotent statement (a second run
changes nothing), to re-run as the migrator role once every API instance runs the new version. It
loops over shops with `app.active_shop_id` set, because FORCE row-level security binds the migrator
as table owner; the integration tests run it as the restricted runtime role to prove that. Lines
are split by the same policy (recorded as `backfill:proportional-line-tax/1`). A return is `BALANCED`
only when every line is classifiable, its lines add up to its total and its payout is evidenced — a
refund destination, or exactly one credit note of the payout amount issued for it; the receivable leg
links its ledger entry when exactly one matches. Anything else is `NEEDS_REVIEW` with its reason and
no legs: a settlement is never inferred. A return an old instance records after the migration has no
snapshot and exports as needing review until the script runs. A `NEEDS_REVIEW` return cannot yet be
resolved in the app, so its periods stay refused for Tally until one is posted. Rollback: keep snapshots readable and
refuse Tally return export; never restore the unbalanced voucher builder.

## Offline Invoice Arithmetic

**Contract per invoice.** Every offline sale is frozen in the browser under a calculation contract and
synced with it. `calculationVersion` absent → `legacy`; `1` → `v1`; anything else answers NeedsReview
with `Sale.OfflineCalculationVersionUnsupported`. The version is part of the request hash only when
present, so an unversioned retry keeps the hash it was first synced with.

**Validation.** `OfflineInvoiceArithmeticValidator` recomputes each line (gross → pre-tax → item
discount → allocated sale discount → taxable → tax → total) and the header (`subtotalBeforeDiscount`
= Σ pre-tax, `totalDiscountAmount`, `totalTaxAmount`, `totalAmount`) from the invoice's own frozen
inputs — price, quantity, tax rate, inclusive flag, override type/value and the rule percentages it
carries — and compares every amount exactly. It never reads today's catalog or rule activity; those
stay variance warnings (`offline_sync.pricing_variance`, `offline_sync.discount_variance`). It runs
after the structural and lease checks and before `ISaleLineValidator`, entity construction, stock
consumption and ledger posting. Inputs are checked first: defined enum values, tax rate and
percentages in [0, 100], non-negative prices and overrides, a rule id with its percentage, a met
threshold, and money inputs and each line's gross (price × quantity) at most ₹100,000,000,000, which
keeps every amount inside `numeric(18,2)` and the legacy replay at single-paise resolution; v1 also
rejects inputs beyond its scales.

- **v1** (`OfflineInvoiceArithmeticV1`, `offline-money.math.ts`): paise, thousandths and basis points
  as integers; every division rounds half away from zero. A tax-inclusive line uses the online identity
  (`SalePricingMath`): pre-tax = price × quantity × 10000 / (10000 + rate bp), total = round(price ×
  quantity) − round(discounts × (10000 + rate bp) / 10000), tax = total − taxable, so an inclusive
  (MRP) price is never exceeded; an exclusive line totals taxable + round(taxable × rate). The sale
  discount goes to goods lines with positive taxable value by floor share capped at that value,
  leftover paise to the largest remainders, ties by line order. `totalBeforeDiscount` is the domain
  definition, Σ(line total + discounts). `tests/contracts/offline-invoice-arithmetic-v1.json`
  (hand-written cases) and `…-v1-corpus.json` (random carts) carry expected amounts from an
  independent exact-rational reference and pin C# and TypeScript to the same results.
- **legacy** (`OfflineInvoiceArithmeticLegacy`): an exact replay of the unversioned browser calculator,
  which ran on IEEE doubles and rounded with `Number(x.toFixed(2))` (half away from zero on the exact
  binary value, reproduced with `BigInteger`), with the final goods line taking the residual sale
  discount. `totalBeforeDiscount` may be Σ pre-tax (what that browser sent) or the domain definition.
  `tests/contracts/offline-invoice-arithmetic-legacy-corpus.json` holds payloads priced by that
  calculator (commit `41015861565d`) and must keep replaying.

**Quarantine.** A mismatch records one `ReconciliationIssue` (`ArithmeticConflict`, code
`Sale.OfflineArithmeticMismatch`, an amounts-only summary within 1000 characters) and answers
NeedsReview with `reviewCaseId` (the issue id) and up to 100 `mismatches` `{path, supplied, expected,
code}`; codes are `amount_mismatch`, `discount_exceeds_basis`, `scale_exceeded`, `unsupported_value`,
`rule_evidence_missing`, `rule_threshold_not_met` and `unsupported_version`. Nothing is posted and the
payload is never rewritten. The device keeps the original frozen invoice (NeedsReview records are not
resent or recalculated) and lists it on the sales page with the reason, differences and case id.
Every NeedsReview result now carries its case id, and a resend that is held for the same reason
returns the open case (`IReconciliationIssueRepository.FindOpenIssueIdAsync`) instead of filing
another.

**Posted sales.** `scripts/db/audit-offline-sale-arithmetic.sql` is a read-only report of posted
offline sales whose header disagrees with their lines or whose lines break taxable + tax = total;
it runs shop by shop under the tenant policies and changes nothing.

## Offline Reconciliation

Offline sales that cannot post cleanly become reviewable **cases** instead of silent drops, and the
original submission is always kept. Routes live under `api/offline-reconciliation`
(`OfflineReconciliationController`).

**Envelopes.** Sync records an `OfflineSubmission` (`offline_submissions`) before it attempts to
post, after only the id, invoice-number and storable-text checks (`StorableText`: a NUL or an unpaired
surrogate in any text field answers per-sale `Sale.OfflineTextInvalid`; in the request's `deviceId` it
fails the whole request). The row holds the device, `clientSaleId`, invoice number, the
payload hash (`OfflineSaleSyncIdempotencyHasher`), the serialized command (`jsonb`), the claimed sold
time, `ReceivedAt`, the `Source` (`Sync`, `Recovery`), the submitting and asserted/original issuer and
a one-time `SaleId` link. It is unique on `(shop_id, device_id, client_sale_id, payload_hash)` and
inserted with `ON CONFLICT DO NOTHING`: an identical resend returns the first row, so `ReceivedAt` is
the trusted receipt time; a changed payload is a second row. Envelopes are never updated or deleted.
Both new tables are tenant tables with RLS.

**Timing rule.** `OfflineIssueTimingPolicy` judges the historical lease (found regardless of expiry)
against the first `ReceivedAt`, with a symmetric 5-minute clock-skew allowance. In order: claimed time
before the lease -> `IssueTimeBeforeLease`; more than 5 minutes after receipt -> `IssueTimeInFuture`;
fiscal year differs from the lease -> `IssueFiscalYearMismatch`; at/after expiry ->
`IssuedAfterLeaseExpiry`; first receipt at/after expiry -> `HistoricalLeaseReviewRequired`; otherwise
accepted. A plausible clock value never passes alone: only a first receipt before expiry is a trust
signal. Posting is `IOfflineSalePoster` (stages, never saves); a held result files one case per
envelope (`FindOpenIssueIdAsync(..., offlineSubmissionId)`). When the envelope has no open case but an
open legacy case (no envelope) exists for the same shop, client sale id, device, type, code and message,
`OfflineSaleVarianceAnalyzer` adopts it with `AttachEvidence(invoiceNumber, envelopeId)` instead of
filing a duplicate (`FindOpenLegacyIssueAsync`).

**Issuer and device history.** New queued payloads carry `issuedByUserId` (hashed only when present).
Absent = legacy (attributed to the submitter, issuer "not recorded"); equal to the submitter =
verified; different = held (`IssuerNotSubmitter`). `invoice_leases.reserved_by_user_id/session_id`
record who reserved a range. For a recovery row an asserted issuer is verified for a device when they
hold or held a membership (any status) and reserved a lease on it or synced from it. Never a member:
row rejected (`Recovery.IssuerMismatch`). Member without device history, or no issuer: envelope kept,
case escalated (`IssuerUnverified` / `ProvenanceUnknown`), nothing posted. People are named only
through shop membership; a non-member renders with an empty `displayName`.

**Actions** (`ReconciliationActionPolicy.AvailableActions`). Owners and managers get every resolution
the case's evidence supports, whether the case is Open or Escalated, plus `Escalate` while it is Open.
Staff get only `Escalate`, on Open cases they created (`POST resolve` is open to every member; the
policy refuses anything else with 403 `Reconciliation.ActionForbidden`). Resolved cases get nothing.
Resolutions, in the order offered:

| Action | Offered when |
|---|---|
| `AcknowledgeVariance` | Pricing / discount / customer variance, or a `ValidationConflict` with code `offline_sync.validation_variance` (keyed on the code: idempotency conflicts share the type) |
| `LinkStockCorrection` | Stock variance with item, batch and an unresolved quantity above zero |
| `RetryHistoricalLease` | The case has its envelope, the envelope has an attributable user, and the code is `HistoricalLeaseReviewRequired`, `RecoveryReviewRequired`, `IssuerNotSubmitter` or any timing code (`IssueTimeBeforeLease`, `IssueTimeInFuture`, `IssueFiscalYearMismatch`, `IssuedAfterLeaseExpiry`) |
| `LinkExistingSale` | Any case with its envelope, whatever its type or status, unless the envelope is already linked to the case's own sale (a variance filed with the sale it describes); and legacy held cases with neither envelope nor sale (types InvoiceConflict, ValidationConflict, ArithmeticConflict, IssueTimingReview, ProvenanceReview, RecoveryReview) |

`LinkStockCorrection` needs an unvoided same-shop/item/batch `FoundStock` or `OtherGain` increase worth
at least the unresolved quantity, not linked elsewhere (partial unique index); it writes one `Out`
movement and leaves the sale untouched. `LinkExistingSale` checks the sale through
`ReconciliationLinkRules.CheckSaleAsync`: with an envelope the sale must carry the envelope's device,
client id and payload hash (for `Sale.IdempotencyConflict`, the case's own sale and an earlier envelope
with that sale's hash); a legacy case without an envelope is checked on identity only (same device and
client sale id as the case). `RetryHistoricalLease` replays the frozen envelope with its claimed
`soldAt` unchanged (the reason is mandatory and the console shows the claimed time, first receipt and
lease window): it links an identical existing sale, refuses a reused invoice number, or posts through
`IOfflineSalePoster` and commits sale, envelope link and resolution together; a held outcome answers 409
`Reconciliation.ReplayRejected` and changes nothing. The reason must be storable text (a NUL or an
unpaired surrogate fails validation with `Reconciliation.ReasonInvalid`; through the HTTP pipeline that
is a 400 `Validation.Failed` naming the `Reason` field). Every transition appends a
`reconciliation_case_events` row. There is no edit-price, renumber or delete action.

**Link candidates.** `GET {caseId}` (detail only, never the list) fills `linkCandidates` for the link
actions the viewer is offered, so the console never asks for a typed id. For `LinkStockCorrection`:
each unlinked, unvoided `FoundStock`/`OtherGain` increase on the case's item and batch that covers the
unresolved quantity (`GetUnlinkedIncreasesAsync`, label = adjustment number). For `LinkExistingSale`:
the sale with the case's device and client sale id, plus for `Sale.InvoiceNumberAlreadyUsed` the sale
holding that invoice number, each kept only when `CheckSaleAsync` would accept it (label = invoice
number). The envelope DTO also carries `leaseReservedAt` / `leaseExpiresAt` from `OriginalLeaseId`.

**Concurrency and idempotency.** Resolve and recovery run through `IFinancialOperationExecutor`
(types 7 `ReconciliationResolution`, 8 `OfflineRecoverySubmission`): a replay returns the stored
response, a changed payload under the same key answers 409 `Operation.PayloadMismatch`. Resolve locks
the case `FOR NO KEY UPDATE`; a stale `expectedVersion` (`xmin`) or a concurrent commit answers 409
`Reconciliation.CaseVersionConflict`. Another shop's case is invisible under RLS (404). The list
clamps page to at least 1 and pageSize to 1-100.

**Recovery bundles.** `POST recovery-submissions` (active owner/manager) takes
`{operationId, bundle: {format: "intelibill.offline-recovery", version: 1, shopId, deviceId, exportedAt,
checksum, invoices[]}}`: 1-100 invoices, request at most 2 MiB (Kestrel answers 413); the console splits
larger exports into several complete bundles. `deviceId` must be storable text and every invoice number
must be leased to it. `checksum` is the lowercase hex SHA-256 of the exact UTF-8 text of the `invoices`
array (client `JSON.stringify`, server `JsonElement.GetRawText()`); enum names or numbers are accepted.
Invoices are read exactly as the sync endpoint maps them: every line becomes a `Goods` line with no
`ServiceId`, whatever the file says. Bundles hold no tokens or session data. Structure and arithmetic
are checked first (a bad row is rejected and nothing is stored). Then, in order: an envelope that is
not yet linked to a sale and still has an unresolved case (from an earlier sync or bundle, any type)
reuses it (`FindUnresolvedByEnvelopeAsync`; result `ReviewOpened` or `Escalated` with that case id, never
a second case); an identical sale already posted links the envelope and nothing else happens (a different
one opens a `ValidationConflict` case); an envelope with no attributable user is escalated at intake
(`ProvenanceUnknown` or `IssuerUnverified`); an inconsistent invoice opens an arithmetic case;
`OfflineIssueTimingPolicy` runs against the envelope's first `ReceivedAt` exactly as sync does, and any
code other than `HistoricalLeaseReviewRequired` opens an `IssueTimingReview` case with that code;
otherwise the bundle opens a review (`RecoveryReviewRequired`).
The cashier's access is never restored.

**Invoice leases.** `ReserveInvoiceLeaseCommandHandler` sets
`expiresAt = min(reservedAt + 7 days, next 1 April 00:00 IST)` (the fiscal year as `FiscalYear.ForDate`
reads it, on the India business calendar), so a lease never spans a fiscal-year boundary and an invoice
billed on 1 April or later under an old range is held for review rather than posted silently. Ranges
come from the shop's fiscal series (see § Issued Invoice Documents and Fiscal Numbering).

**Rollout and rollback.** Migration `AddOfflineSubmissionReconciliationAudit` only adds tables and
columns; existing cases take `status` from `is_resolved` and have no envelope ("original envelope not
recorded"). Legacy held cases can be closed with `LinkExistingSale` once the invoice posts, and a later
sync of the same hold adopts them; the console offers a one-time "send to server for review" for local
held invoices whose case kept no envelope. Rollback removes the routes and menu entry;
envelopes, cases, events and leases stay and invoice ranges are never recycled.

**Out of scope.** A device-revocation registry, closed accounting periods (backlog 27), offline
attestation of the issue time, and reprint polish (backlog 21).

## Issued Invoice Documents and Fiscal Numbering

Every sale is issued as a document whose particulars are frozen at issue. Plan:
`docs/superpowers/plans/2026-11-03-gst-invoice-contract-and-numbering.md`; the supported matrix for
accountant sign-off: `docs/gst-invoice-capability-matrix.md`.

**Tax profile.** A shop declares its GST registration (`PUT api/shops/{shopId}/tax-profile`, owner, or
`taxProfile` on `POST api/shops`): `TaxRegime` (`Regular`, `Composition`, `Unregistered`), GST
`StateCode`, `LegalName`, `InvoiceSeriesCode` (default `A`) and, for Regular, an `EInvoiceStatus`
attestation (`BelowThreshold`, `Exempt`, `Required`) with who/when. `Shop.DeclareTaxProfile` checks
the GSTIN (shape, state, check character via `Gstin`) against the regime and state; the regime is
never inferred from the GSTIN. Existing shops migrate as `NotDeclared` and can neither sell nor reserve
offline ranges until they declare. Once declared, the GSTIN belongs to the profile: `PUT api/shops/{id}`
keeps it when omitted and refuses a different one (`Shop.GstinManagedByTaxProfile`). Customers carry an
optional checksum-valid `Gstin` and `StateCode` (defaulting to the GSTIN's state); a GSTIN makes their
invoices B2B. On customer edit an absent `gstin`/`stateCode` keeps the recorded value and an empty
string clears it, so clients that predate the fields cannot erase them.

**Capability matrix.** `InvoiceDocumentBuilder.CaptureSellerProfile` refuses an undeclared profile
(`Invoice.TaxProfileRequired`), an invalid seller GSTIN (`Invoice.SellerGstinInvalid`) and an
e-invoice-required registration (`Invoice.EInvoiceUnsupported`: IRN/QR is not integrated). `Build`
then picks the document: Regular → `TaxInvoice` (all lines rated), `BillOfSupply` (all 0%) or
`InvoiceCumBillOfSupply` (mixed, Rule 46A, unregistered buyers only: a registered buyer's mixed
basket is refused with `Invoice.MixedSupplyToRegisteredBuyer`); Composition → `BillOfSupply` with the composition
declaration, refusing any tax (`Invoice.CompositionCannotCollectTax`) and any interstate supply;
Unregistered → `Receipt`, refusing any tax. Regular also needs a registered buyer's name and address,
an unregistered buyer's name, address and state at ₹50,000 taxable (Rule 46(e)), 4-digit HSN on B2B
lines below the e-invoice threshold and 6-digit HSN on every line of an `Exempt` registration (which
also carries the Rule 46 exemption declaration). Every line must satisfy `total = taxable + tax` and
the lines must sum to the sale (`Invoice.AmountsDoNotReconcile`); amounts are never recomputed.

**Place of supply and components (`component-split/1`).** `RecordSale` takes an optional
`placeOfSupplyStateCode`, used as given. Absent, goods sold at the counter are supplied in the seller's
state (IGST Act s.10(1)(c)) and services in the recipient's recorded state — the GSTIN's for a
registered buyer — else the seller's (s.12(2)); a sale of both whose places differ must state one
(`Invoice.PlaceOfSupplyRequired`).
Intrastate tax splits into CGST + SGST, or CGST + UTGST in a union territory without a legislature
(04, 26, 31, 35, 38), each at half the rate, CGST taking the odd paisa; interstate tax is IGST at the
full rate. Codes outside the domestic table (96, 97) are refused. Rounding adjustment is explicit (0).

**Snapshot.** `IssuedInvoiceSnapshot` (version 2) is stored once as `jsonb` in `sales.invoice_document`
(camel case, enum names; `Sale.AttachInvoiceDocument` refuses a second write) with document type,
issue time, business date, fiscal year, series, number, seller and buyer blocks, place of supply,
reverse charge (always false), lines with HSN/SAC, UOM (`NOS`/`NA` until per-item UOM), taxable value
and components, totals, declarations and signature mode. `GET api/sales/{id}` returns it as
`document`; `isLegacyDocument` marks sales issued before it (no snapshot, never backfilled from live
master data). Print, reprint and Tally read the snapshot, not the shop or customer.

**Numbering.** `IInvoiceNumberAllocator` is the one numbering authority. Numbers are
`{series}/{yyyy}/{counter:D6}` (`A/2627/000001`), at most 16 characters (Rule 46); the counter's
capacity is what still fits (`Invoice.SeriesExhausted`). `invoice_sequences` holds one row per shop,
fiscal year and series, unique also on `(seller_registration_key, fiscal_year_start, series_code)`
where the key is the GSTIN (or `shop:{id}` without one), so branches under one GSTIN cannot share a
series (`Invoice.SeriesRegisteredToAnotherShop`) and a shop whose registration changed mid-year must
pick a new series (`Invoice.SeriesRegistrationChanged`). The allocator inserts the row
`ON CONFLICT DO NOTHING` and locks it `FOR UPDATE` inside the caller's transaction: `RecordSale` now
always runs in one transaction and takes its number after pricing and customer checks, so concurrent
postings serialize per series and a refused or failed posting rolls the counter back — no gaps.
Offline ranges come from the same row (`ReserveRangeAsync`), so online and offline numbers never
overlap; a range's unused tail stays unused. Idempotent replays return the original number. Legacy
counters keep their `INV-yyyy-yy-` prefix under series `''` and key `legacy:{shop}` until their leases
expire.

**Business date.** `BusinessCalendar` reads instants in IST (fixed UTC+05:30). `FiscalYear.ForDate`
uses it, so a sale at 00:30 IST on 1 April belongs to the new year; lease expiry caps at 1 April
00:00 IST; the offline fiscal-year timing check and Tally voucher and reference dates use the same
calendar. Export date ranges, customer ledger dates and the Excel/PDF exports still read UTC dates
(backlog 27).

**Offline.** A lease freezes the seller profile it was reserved under (`invoice_leases.seller_profile`);
sync and reconciliation replays issue the document under it (the current profile for legacy leases).
A device has already handed the invoice over, so if the matrix refuses it the sale still posts, without
a document, and an informational `DocumentReview` case (type 11, acknowledged in the console) records
why. Device-side printing still uses its local layout (backlog 21).

**Exports.** Tally sales vouchers post the snapshot's components (`Output CGST 9%`, `Output SGST 9%`,
`Output UTGST 9%`, `Output IGST 18%`, all under Tax Payable; component rates keep every digit, so
half of 0.25% is `0.125%`); legacy sales keep `Output GST {rate}%`. A return of a documented sale
reverses into the same component ledgers: `ReturnAdjustmentEntry.DocumentTaxComponents` splits the
returned tax per rate with the document's place of supply and `component-split/1`. Credit-note
documents themselves are unchanged (backlog 17).

**Rollout and rollback.** Migration `AddIssuedInvoiceSnapshotsAndFiscalSeries` adds columns, backfills
legacy counter keys shop by shop (FORCE RLS) and replaces the per-shop-year index. Owners must declare a
profile before their next sale or lease. Rolling back the API leaves snapshots as readable data; the
migration's `Down` fails once a shop has opened a fiscal series beside its legacy counter, so roll
forward. Out of scope: IRN e-invoicing and B2C dynamic QR, exports/SEZ, reverse charge, cess,
device-side GST printing, credit-note documents. (Per-item units now live in § Quantity Precision.)

## Business Dates and Posting Periods

**Shop zone.** `shops.business_time_zone` (`varchar(64)`, NOT NULL, default `Asia/Kolkata`) names the
zone a shop's days are read in. No API changes it in this release (`GET` shop returns it read-only), so
every shop is on India time. `BusinessTimeZones.Resolve` caches zones; a host without tzdata still serves
the default zone through its fixed UTC+05:30 offset (India keeps no DST), any other unknown id throws.
`IBusinessClock` is the only source of "today" and of a posting's `BusinessDay(Date, TimeZoneId)`; code
never adds a fixed offset itself (`BusinessCalendar` is the pure IST helper under it).

**Stored business dates.** `sales.business_date` + `business_time_zone` and `sale_returns.business_date` +
`business_time_zone` + nullable `voided_business_date` are written once, with the posting, from the shop's
zone at that moment. Customer and supplier ledger `DateOnly`s come from the same date; user-entered
payment and expense dates already are business dates and are never reinterpreted. Sale and return reports,
exports and the P&L filter these columns with an inclusive `DateOnly`
range, so a later zone change (or a tz rule change) can never move a posted fact between days or fiscal
years. A sale at `2026-10-20T20:30:00Z` (02:00 IST) belongs to 21 October everywhere; `18:29:59Z` and
`18:30:00Z` on 31 March fall in fiscal series `2526` and `2627`. Returns post on the date of their
processing, a void posts a reversal on its own `voided_business_date`.

**Windows for instants.** Tables that only have an instant (inventory adjustments, purchase orders,
customer metrics over sales) use `IBusinessClock.GetUtcWindow(from, to, zone)` -> `UtcDateWindow
[StartInclusive, EndExclusive)`: half-open, so the next day's midnight is never in two reports. No
repository computes its own offset.

**Fiscal year.** `FiscalYear.ForBusinessDate(businessDate)` picks the invoice series of an online sale
and of an offline sale posted without a lease (the posting's own business date). Known limitation:
invoice-lease reservation (`ReserveInvoiceLeaseCommandHandler`), the lease expiry cap and the offline
timing check (`OfflineIssueTimingPolicy`) still use the IST `FiscalYear.ForDate(instant)`. The two
agree while IST is the only zone and must be moved to the shop zone together when a second zone ships.

**Posting periods.** A period is one calendar month of the business calendar: `posting_periods(shop_id,
start_date, end_date, state Open|Closed, version xmin)`, unique `(shop_id, start_date)`, RLS-forced like
every tenant table. All months start Open and no row exists until first touched: the fence and
close/reopen both create the row on demand (`INSERT ... ON CONFLICT DO NOTHING`), so a missing row can
never bypass the fence. `GET api/posting-periods?fiscalYear=` (owner/manager) lists the twelve months of
the fiscal year (default: the current one). `POST api/posting-periods/{yyyy-MM}/close|reopen` is
**Owner-only**, checked against the live membership (never the token's role claim) before the executor and
again under the period lock. Both need a `reason` (1-500 chars) and an `operationKey` (executor types
`PostingPeriodClose = 11`, `PostingPeriodReopen = 12`; resend replays, changed payload -> 409
`Operation.PayloadMismatch`). A month closes only after it ended (`EndDate < today` in the shop zone) ->
409 `PostingPeriod.NotEnded`; closing a closed (or reopening an open) month -> 409
`PostingPeriod.AlreadyClosed` / `AlreadyOpen`. Every transition appends a `posting_period_events` row
(`PostingPeriodAction` Closed|Reopened, reason, actor, time).

**Fence.** `IPostingPeriodPolicy.AuthorizeForWriteAsync(shopId, date)` (or a set of dates: distinct
months, ascending, first error wins) needs an open transaction, ensures the row, sets a 5 s
`lock_timeout`, takes `FOR SHARE` on the period and refuses a Closed one with 409 `PostingPeriod.Closed`.
Timeout (55P03) -> 409 `PostingPeriod.Busy`. The caller's own `lock_timeout` is read first and put back
with `set_config(..., true)` afterwards, so a caller with a bigger budget keeps it. Close/reopen takes
`FOR UPDATE` on the same row (first-touch insert inside the same budget), so a posting that passed the
fence blocks the close until it commits, and a close that committed first refuses the posting: nothing
commits into a month after its close. The fence comes before every business row lock, but after the
operation receipt. Order: operation receipt reservation -> (reconciliation case row lock, for replays) ->
period fence -> customer/supplier -> sale/payment receipt -> credit notes -> batches (the customer lock
in Sale Return Serialization, Payment Receipts and Customer Credit Policy is the first *business* lock,
not the first lock).

**Fenced workflows (date checked).** Online sale and offline invoice staging (issue business date); sale
return record and void, payment reversal, receipt cost correction, void batch, stock count (today);
adjustment void (today and the business date of the adjustment's `PerformedAt`); supplier payment
reversal (today, plus the `ExpenseDate` of every expense in the chain it voids); supplier reassignment
(today and the `EntryDate` of every GoodsReceived/CostAdjusted ledger entry it rewrites); customer and
supplier payment (`paymentDate`); expense record (`expenseDate`) and correction (both the original's and
the new date's month); add inventory, each bulk-intake row, adjustment create and purchase-order receive
(the business date of the instant actually written, which is the client-supplied `performedAt` /
`receivedAt` when given, else now, so a backdated stamp cannot post into a closed month). A resend that
meets a busy fence in bulk intake is reported per row as retryable. A customer payment reversal posts a
dated reversal entry (today) and is fenced on that date only. A correction of a closed month's posting
(adjustment void, supplier payment reversal, supplier reassignment) is refused with `PostingPeriod.Closed`
when the original's month is closed. A **return or void of a sale from a closed month is allowed**: it
posts on today's date and references the original, so closed reports do not change.

**Not fenced.** Payment allocations (they move no ledger date); credit-note void; the catch-up stock
movement that a reconciliation `LinkStockCorrection` posts (dated today, so it can only land in a month
that closes between the check and the write).

**Late offline invoices.** An invoice keeps its issue time and business date. If its month is closed,
`OfflineSalePoster` holds it as `ReconciliationIssueType.ClosedPeriodReview = 12` (code
`PostingPeriod.Closed`, message names the month) with a review case: it is never redated and never
renumbered, and no sale or stock posts. The envelope stays; a resend with an unresolved
`ClosedPeriodReview` case returns that case and stages nothing. `RetryHistoricalLease` is offered for it;
the replay re-runs the fence, so it succeeds (original number, original `sold_at`, original business
date) only after an owner reopens the month, and answers 409 while closed. A busy fence rejects the
invoice for a plain resend.

**Rollout.** Migration `AddBusinessDatesAndShopTimeZone`, in this order: adds `shops.business_time_zone`
(NOT NULL, default) and the nullable `business_date` / `business_time_zone` / `voided_business_date`
columns, backfills existing rows (Asia/Kolkata) shop by shop, sets NOT NULL, installs
`BEFORE INSERT OR UPDATE` triggers that date rows written by pods that predate the columns (rolling
deploy), then adds the indexes; `AddPostingPeriods` adds the two tables. The whole migration is one
transaction: the `ADD COLUMN` takes ACCESS EXCLUSIVE on `sales` and `sale_returns` and the backfill
`UPDATE` runs under it, so sale and return writes block for the duration (fine at launch sizes). Deploy
migrations, then pods. **Do not close any posting period until every pod runs this build:** pods of the
previous build have no fence and would keep posting into a closed month. Afterwards run `scripts/db/backfill-business-dates.sql` (shop by shop under RLS,
idempotent, a no-op when nothing is NULL). Nothing closes automatically; owners close months after review.
Reports that straddled UTC/IST midnight may differ from previously published ones by early-morning sales:
say so in release notes.

**Rollback.** Old code ignores the new columns and tables. Never recompute stored dates with the UTC
convention; dropping the migrations drops the columns and tables, the instants are untouched.

**Deferred.** A web UI for period close (API only for now), mobile, and per-shop zone selection. Till/session close now takes the original posting-period fence before its session lock; see Till Sessions and Counted Close.

## Quantity Precision

- **The item owns its unit and scale.** `Item` carries `UnitCode`, `QuantityScale` (0 to 3) and
  `UnitReviewRequired`. `QuantityUnits` (`Domain/ValueObjects`) is the catalog pinned by
  `tests/contracts/quantity-units-v1.json`: a label (trimmed, upper-cased) matching an alias resolves to a
  code with a fixed scale and document unit (UQC), for example `KG` is scale 3 and `KGS`, `PCS` is scale 0
  and `NOS`, `G`, `ML` and `CM` are whole-number units. A custom label stores `UnitCode` null and the
  merchant's `QuantityScale` (default 0). `Item.DocumentUnit` is computed: the catalog unit, else `NOS` at
  scale 0 and `OTH` above. Storage stays `numeric(18,3)`; no migration changes a quantity.
- **One policy.** `QuantityPolicy` allows at most 3 decimals and a scale of 0 to 3. Trailing zeros from
  `numeric(18,3)` are not precision (`2.000` is a whole number). It returns generic `Quantity.*` errors and
  never rounds, truncates or clamps what a person entered.
- **Callers name the workflow.** Sale lines, sale returns, intake, counts and adjustments check the
  resolved item's policy before building anything and translate the generic error into
  `Sale.`, `SaleReturn.` or `Inventory.QuantityPrecisionExceeded`. FluentValidation failures answer
  `Validation.Failed` with the code in `errors[].code`; handler errors answer the code as the `title`.
  Offline sync runs the same sale line validator, so online and sync refuse over-precision identically.
- **Returns.** The effective scale is `max(item scale, scale of the sold quantity)`, so a legacy
  fractional line stays returnable even when the item is now classified whole-unit.
- **Unit changes.** `Item.ChangeUnit(uom, quantityScale, history)` takes an `ItemStockHistory`
  (movements, batches or sale lines exist). With history, changing the unit or scale is refused with 409
  `Item.UnitChangeWithHistory`; a relabel that keeps code and scale (`kg` to `KGS`) is allowed. A legacy
  item flagged `UnitReviewRequired` may be classified once to a scale that covers its on-hand
  stock, which clears the flag. Saving a flagged item at the unit and scale it already has clears the flag
  when that scale covers the stock on hand (otherwise the flag stays and the other edits save). An item
  nobody has classified yet (`UnitCode` null) saved with a label naming the same catalog unit as its
  stored label, at a catalog scale that covers the stock on hand, is classified, not changed: no 409.
  The update handler loads the stock history for a flagged item too.
- **Documents.** `InvoiceDocumentBuilder` prints the item's document unit on each goods line (`KGS`,
  `MTR`, `NOS`), and a service line still prints `NA`. Offline sync takes the document unit from the live
  `Item.DocumentUnit` at sync time, not the device's frozen value. The only case where a reprint can
  differ from the device copy is a review-flagged legacy item reclassified between the offline sale and
  its sync.
- **Out of scope.** Purchase-order quantities stay integer (backlog 30); receiving a purchase order still
  passes the item policy. Carton to piece conversion, scale integration and barcode-embedded weight
  (backlog 28) are not advertised.

**Rollout and rollback.** Migration `20261105155114_AddItemQuantityPrecision` adds `unit_code`,
`quantity_scale` and `unit_review_required`, maps known labels through the catalog and flags an item
classified whole-unit whose recorded quantities carry decimals. Defaults keep old instances working. Once
every API instance runs the new version, run `scripts/db/backfill-item-quantity-precision.sql` once as
the migrator role for items an old instance created in between (idempotent: an item with a `unit_code`
is never touched). Both the migration and the script run shop by shop with `app.active_shop_id` set,
because FORCE row level security binds the migrator as table owner; a backfill without a shop context
updates no rows. `ItemQuantityPrecisionUpgradeTests` applies the migration and the script as a
`NOSUPERUSER NOBYPASSRLS` owner role to prove it. Rollout window caveat: an old instance that relabels an
already-mapped item (for example `kg` to `pcs`) updates `uom` but not `unit_code`, and the script skips the
row because `unit_code` is set. Spot such rows with
`SELECT id, uom, unit_code FROM items WHERE unit_code IS NOT NULL` compared against the catalog (a
label that no longer resolves to its `unit_code`), and fix them by clearing `unit_code`, `quantity_scale`
and `unit_review_required` on those rows (per shop context) before re-running the script; stocked items
should be reviewed by the merchant rather than silently remapped. Rolling back means deploying the previous API: it ignores the new columns, fractional
data stays valid in `numeric(18,3)`, and no quantity is downgraded.

## Test Structure

| Suite | Path | Count |
|---|---|---|
| Domain unit | `tests/backend/unit/Intelibill.Domain.Unit.Tests/` | 354 |
| Application unit | `tests/backend/unit/Intelibill.Application.Unit.Tests/` | 1563 |
| API unit | `tests/backend/unit/Intelibill.Api.Unit.Tests/` | 576 |
| Integration | `tests/backend/integration/Intelibill.Integration.Tests/` | 627 |
| **Total** | | **3120** |

Integration tests use `WebApplicationFactory<Program>` backed by **Testcontainers** (`Testcontainers.PostgreSql`) — Docker required. Non-integration suite: 2493 passing.

## Recorded-cost Profit and Discount Economics

Online sale preview and posting use the same pricing calculator. Every goods line must retain
post-discount **tax-exclusive revenue at least equal to rounded quantity × recorded batch cost**.
Collected output tax never funds a discount. `PurchaseTaxIncluded` is not evidence of recoverable
purchase tax: new preview, sale cost snapshots and current-stock valuation preserve the recorded
`CostPrice`. Existing sale/return cost snapshots, tax, invoice documents and idempotency identities
are immutable. There is no below-cost role override. Rejections retain existing `SalePricing.*`
codes and describe requested net revenue, recorded cost and loss.

P&L rows and summary add `economics.version = 2`, with `costBasis = recorded-sale-cost/1` on the
summary. Net sales exclude collected tax. COGS uses frozen goods sale costs, reversed only by
restockable returns. Independent stock losses post on their original instant and their negation on
`VoidedAt`, selected through the shop's half-open business-day UTC window. Returned-goods wastage
is diagnostic **already included in COGS**, never another stock-loss deduction. Return and return
void figures use the accounting reader's signed posted snapshots and their own business periods.
Later purchase receipt cost corrections do not revalue frozen COGS.

`GrossTradingProfit = NetSalesExcludingTax - Cogs - StockLoss`. Gross margin divides this amount
by net sales, while markup divides it by COGS. Both round to two decimals away from zero; exactly
zero denominators are null, and negative denominators retain their signed arithmetic. Summary
ratios use period aggregates, never an average of row percentages. Paging does not change totals.
Legacy `MarginPercent` and `AverageMarginPercent` keep their cost denominator (markup); legacy
`NetProfitAfterTax` retains its tax-exclusive trading formula and does not mean after-income-tax
or operating profit. New web/XLSX readers use the version2 projection. Dashboard adds this same
period economics summary and its own `GrossTradingProfitChangePercent`; old `NetProfit` remains.

Expenses carry immutable `ExpenseTreatment`: Unclassified(0), Operating(1), SupplierSettlement(2),
CapitalOrOtherExcluded(3). Omitted ordinary request treatment defaults to Unclassified; corrections
inherit omission. Generic requests cannot assert supplier settlement. Supplier-payment provenance
stamps settlement, and correction descendants retain both treatment and supplier ledger link.
A differing supplier treatment is refused; normal classification changes use the existing audited
replacement correction and both-month posting fences. Voided original expenses are excluded;
the replacement contributes on its own `ExpenseDate`. Category names establish no treatment.
Supplier settlements and capital/other excluded rows are never deducted again from trading profit.

For a full-period report with classified active expenses, reviewed return accounting and no
unmodeled service costs, operating profit is trading profit minus Operating expenses. Otherwise
`OperatingProfit` is null, with status precedence `FilteredActivity`, `UnclassifiedExpenses`,
`AccountingReview`, `ServiceCostsUnmodeled`; all applicable limitation codes are included. Search
or type filters also make `OperatingExpenses` null: whole-shop expenses are not charged to an
individual invoice. Unclassified counts/amounts are period-wide diagnostics. A complete period
with no activity returns real zero amounts and null ratios. These recorded-cost trading metrics
are not a general ledger or audited/after-income-tax profit; purchase-tax recoverability, modeled
service costs and receipt-cost revaluation remain explicit limitations.

Rollout: apply `20261107050412_AddExpenseTreatment` with the schema migrator before the API/web.
Its safe default is `0`. It backfills supplier-linked roots and same-shop correction ancestry using
cycle-safe recursive UNION, setting each shop context under FORCE RLS. The idempotent
`scripts/db/backfill-expense-treatment.sql` can be rerun; it restores the caller's shop context.
Remaining ordinary expenses require explicit operator classification. Deploy additive API fields
before the web and compare a representative closed period: labels/completeness and restored dated
loss/void history can differ, while frozen invoices/costs remain unchanged. Rollback can retain
additive columns and legacy readers; never restore the unsafe tax-inclusive discount capacity.

Fractional return costs in version2 replay the full affected sale-line history, including postings
outside the report window. A line's original rounded sale cost is allocated cumulatively across
active returned quantities; each return receives the change in that rounded cumulative cost.
Restockable and wasted quantities share this physical allocation, so the final residual paise is
assigned once. A void reverses its original allocation exactly and releases its quantity/cost for a
later return. Earlier dated allocations never change when the requested report window changes.
Ties use posting instant, ordinal return number, event kind and return ID. These economic allocations
do not rewrite frozen sale/return snapshots or alter the legacy metric formulas. Numeric report and
XLSX search match the displayed net sales, rounded COGS, loss and trading amounts as well as the
legacy monetary fields; searched operating totals remain unavailable.

## Expense List Filtering

`GET /api/expenses` accepts `search`, `status`, `page` (default 1), and `pageSize`
(default 20, range 1–100). Status names `all`, `active`, and `voided` are
case-insensitive and trimmed; omission or whitespace means `all`. Unsupported names
(including numeric enum strings) return 400 `Expense.InvalidStatus`. Nonpositive
pages and offsets exceeding Int32 return `Expense.InvalidPageNumber`; unsupported
sizes return `Expense.InvalidPageSize`. MVC rejects malformed numeric bindings.
The controller prevalidates with the registered query validator to preserve these
codes through `ToProblemResult`; direct bus callers retain validator protection.

The repository starts with the authenticated active shop, applies the paid-to,
description/category search and status predicates once, then counts and pages
that same query. Ordering is expense date, creation instant, and ID, all descending.
No report arithmetic, expense treatment, or correction provenance changes. Separate
requests still use offset pagination, without a cross-request snapshot guarantee.

## Supplier Returns and Receipt Reversals

Migration `20261107180954_AddSupplierReturnsAndReceiptReversals` must be applied before deploying the API or web. It adds immutable supplier shipment, acceptance and full-reversal facts; the new tenant tables have composite tenant references and ENABLE/FORCE RLS with USING and WITH CHECK. Existing receipts are not inferred to have returns. `purchase_receipt_values.has_supplier_returns` is a monotonic guard written with each shipment. Keep the schema and audit facts during rollback; disable new mutation entry points rather than running Down after posting.

Owner/Manager can read source receipt lots, preview and record physical supplier shipments. A Manager can record only `PendingSupplierCredit`; only an Owner can accept credit or reverse a wholly erroneous receipt. All new writes require an operation key, expected versions and a reason. Authorization precedes operation replay. Operation types14/15/16 use lookup slugs `supplier-return`, `supplier-return-credit`, `purchase-receipt-reversal`.

- `GET /api/purchase-receipts/{receiptValueId}` provides source versions, original receiving, available stock, returnable ceiling, net retained purchases, payable projections, shipment/reversal history and reversal eligibility reasons.
- `POST /api/supplier-returns/preview` is advisory. `POST /api/supplier-returns` records1–100 distinct same-supplier source lots atomically; an Owner may explicitly accept proposed credit in that transaction.
- `GET /api/supplier-returns` filters by supplier/receipt/status with paging capped at100. `GET /api/supplier-returns/{id}` includes immutable print facts. `POST /api/supplier-returns/{id}/credit` accepts exactly every shipment line once; explicit zero credit creates acceptance evidence without a zero ledger entry.
- `POST /api/purchase-receipts/{receiptValueId}/reversal` and `POST /api/purchase-order-receipts/{receiptId}/reversal` reverse one eligible lot or the exact entire PO receipt-lot set. A bad lot rolls back the group. Historical PO received/remaining/status do not change.

Returnable quantity is `max(0,min(available,received-alreadyShipped))`; pending shipments count against the ceiling. Each line writes one `SUPPLIER_RETURN` movement (enum9), reduces its original batch and item aggregate under the shared xmin protocol, and touches its receipt version. It is neither a sale nor an independent stock loss. Batch costs and issued sale snapshots remain unchanged. Every shipment blocks later receipt cost correction and supplier reassignment with `PurchaseReceipt.HasSupplierReturns`.

Accepted supplier credit (ledger enum6, persisted `SUPPLIER_RETURN_CREDIT`) is negative and source-linked. It reduces net payable, not the recorded effective receipt cost. Per receipt: `netPayable=effectiveCost-acceptedCredit`; `outstanding=max(0,netPayable-activeAllocations)`; `supplierCredit=max(0,activeAllocations-netPayable)`. Existing payment allocations remain intact. A paid receipt can therefore yield a supplier account credit; there is no automatic cash refund, expense or replacement allocation. The signed supplier ledger already includes the accepted credit, so do not add the receipt-credit projection to the account balance again.

The financial operation executor owns the transaction. Lock order is operation receipt → posting-period fence → supplier → sorted receipt roots → return root for acceptance. The focused lock uses SELECT FOR NO KEY UPDATE without changing receipt xmin and restores lock_timeout; facts are loaded after locking. Explicit receipt writes then protect legacy correction/reassignment/payment races, while batch and aggregate xmin protect quantity writers. Full reversal fences both the receipt's original date and today's date, requires exact untouched receipt quantity and no consumption/count history, shipment or active allocation, and reverses the exact effective total. The existing void route additionally refuses supplier shipments and count history, fences the original period, and records linked reversal facts for resolved receipts.

Printed output is a “Supplier return reference” with “Commercial reference — not a GST credit/debit note”. It uses frozen shop, supplier, item/UQC, quantity and source snapshots plus separate acceptance evidence. There is no legal tax-document claim, supplier-return cancellation, credit reversal, automatic PO reopening or mobile implementation.

Supplier return print rollout also requires `20261107190743_AddSupplierReturnLotSnapshots` before API/web. It adds nullable frozen lot numbers without backfilling live names into historical facts. New shipments capture their original lot number; older return references truthfully fall back to the persisted receipt-value ID.

## Sale Cancellation Policy

`receipt-cash-due/1` is a terminal mistaken-receipt correction. An active Owner/Manager may cancel only an online v2 zero-tax, zero-rounding Receipt from its frozen Unregistered seller in the same open shop-local month. Every original inventory line must be recoverable into its original valid batch and the actor must confirm all goods recovered. Settlement is exactly original due reduction plus explicitly confirmed original paid Cash. Zero-paid sales require no cash confirmation. External paid tenders, applied/redeemed credit notes, any return history, active allocations, unallocated customer receipts or other collection ambiguity, offline origin, services, legacy/mismatched documents and older/closed months are refused. No partial correction, statutory GST note, automatic refund, credit restoration or uncancel is implemented.

`POST /api/sales/{saleId}/cancellation-preview` is read-only and returns policy reasons plus independent rollout state and expected version; it reads period state without creating a row. `POST /api/sales/{saleId}/cancellations` takes a trimmed 1–128 operation key, nonzero expected version, trimmed 3–255 reason, goods confirmation and exact cash confirmation. `GET /api/financial-operations/sale-cancellation/{key}` recovers a committed result. Live membership/Owner-or-Manager and tenant target authorization precede executor replay. The new-operation callback requires both `SaleCancellation:Enabled=true` and exact shop allowlisting; defaults are false/empty. Committed replay remains valid after disablement/month closure. Shape errors are 400, policy/version conflicts 409, posting disabled 503.

`Sale.Status` is Posted 0/Cancelled 1 with existing rows default Posted. `Sale.Version` maps PostgreSQL `xmin` and is an equality precondition. Original number, issued snapshot, amounts, item facts, source/date and idempotency identifiers are never rewritten. Immutable `SaleCancellation`/`SaleCancellationLine` preserve actor, reason, date/zone, policy, frozen seller/buyer/line UQC, original invoice, raw cost, rounded recorded cost and linked effects. One cancellation per sale and one line per original sale item are enforced; tenant composite foreign keys restrict deletion. Both new tables carry `shop_id`, shop-leading indexes and ENABLE/FORCE RLS with USING and WITH CHECK, and appear in `TenantPolicyManifest`.

Document lines have no persisted ordinal link to sale items. Eligibility matches immutable common fields as a multiset, consumes each line once and preserves physical item/batch links. Identical frozen duplicates are interchangeable; ambiguous differing display/UQC facts refuse `SnapshotMismatch`. Sale/document instants compare at PostgreSQL's persisted microsecond precision, accounting for JSON retaining 100ns ticks, without a monetary tolerance or mutation of original facts.

The financial executor owns reservation and transaction: operation receipt → posting-period fence → customer → sale → deterministic batch/item mutations. Authorization does not retain a tracked pre-lock sale; fresh facts/dependencies are loaded after locks. One captured business day controls the fence and cancellation date. Type17 operation `SaleCancellation`, type 6 customer `SaleCancellationCredit` (`SALE_CANCELLATION_CREDIT`) and type 10 stock `SaleCancellation` (`SALE_CANCELLATION`) are appended. Each original line restores its quantity and gets a positive linked stock movement; repeated batches restore the sum. Exact original due adds one ledger credit. All effects, terminal state and replay response commit together. Batch/aggregate xmin catches concurrent quantity changes. Returns and payments share customer→sale locking; cancelled sales have zero current allocatable/returnable balance and explicit cancellation credit, without deleting original issue due. Publish inventory refresh after commit.

`SaleCancellationAdjustmentReader` projects positive immutable magnitudes. P&L, history summaries, dashboard trends/customer metrics and XLSX/PDF/Tally subtract revenue and cost on cancellation business date while retaining the original positive issue-date rows, including adjustment-only windows. Recorded COGS reverses each original line's rounded cost; legacy metrics reverse raw cost products separately. Cancellations have distinct counts/amounts and never become returns, wastage or independent stock loss. Tally uses a validated balanced Journal with original invoice/date: debit Sales, credit customer due and confirmed Cash. Print/export uses frozen facts and “Commercial cancellation record — not a GST credit note.” Positive cash adds “Cash refund is recorded from the operator's confirmation.”

Apply `20261107201224_AddSaleCancellations` before API/web; update all pods/readers before enabling any approved shop. Configuration, review prerequisites, validation and rollback are in `docs/sale-cancellation-rollout.md`. Disable posting to roll back and retain schema/facts/operation replay/readers/guards. Pre-feature binaries and destructive Down after a cancellation are unsafe. This implementation establishes no accountant/legal approval or payment-transfer evidence.

## Till Sessions and Counted Close

Till sessions attach recorded external settlement and non-drawer settlement totals to one physical till, named cashier and shop-local business date. Seven tenant tables (`tills`, `till_sessions`, `till_source_records`, `till_movements`, `till_closes`, `till_close_reviews`, `till_late_postings`) use forced RLS and composite tenant foreign keys. A partial unique index prevents two open sessions per till. Immutable sources are globally unique by shop/type/source/purpose; movements add line and session sequence uniqueness. An all-due sale still appends a source and advances session `xmin`, protecting close from a stale zero-cash boundary.

`TillSessions` defaults to `Enabled=false`, `AllowedShopIds=[]`, `MixedTenderEnabled=false`; enablement requires both gate and exact shop. Mixed tender is unsupported. Explicit new evidence with a disabled gate is refused; absent fields retain legacy behavior/hash. Deploy the five migrations and every API/read/write path before a separately approved pilot. All five Down methods refuse schema rollback. See [Till rollout](till-sessions-rollout.md) for ordered migrations, contracts, verification and operational limits.

`ITillPostingService.AcquireAsync` is called freshly inside the source's transaction after sorted period fences. It locks and reloads the same session used by close, enforces active cashier/manager override and same-day trading, then `AppendAsync` stages immutable source/movement facts without saving. Transaction order is operation receipt → reconciliation case where applicable → sorted posting periods → till/session → customer/supplier → sale/receipt → credit notes → batches/items. Each source owns one save/commit; keyed returns use the coordinator's pre-business-lock hook under the executor's existing transaction. Never reuse an acquired session across commit, rollback or retry.

`TillPostingContext` freezes session and override reason in each source hash. `TenderLine` conserves known external paid/refund amounts; cash tendered minus change equals applied cash. Store credit and receivable changes are separate totals. `TillSettlementEvidence` declares actual money paid/received or explicitly no physical movement. Customer/supplier receipts produce one signed movement; the supplier's linked expense does not produce a second movement. Expense corrections use MoneyPaid for increased cost and MoneyReceived for decreased cost, with absolute delta tender and inverse signed drawer effect; zero delta requires NoPhysicalMovement, and payment/return voids never infer physical recovery from legacy tender or generic Refund. Cancellation retains its existing capability/eligibility and exact confirmed cash refund. Supplier return credits and purchase receipt reversals have no drawer effect.

`POST /api/tills` creates physical tills (Owner/Manager). Opening at `POST /api/tills/{id}/sessions` requires explicit float and cashier. Cash movement, close and review live at `/api/tills/sessions/{id}/{cash-movements|close|review}` and require keys/version. Explicit close count is mandatory, including zero; nonzero variance requires a reason. Staff variance closes await append-only manager review. Historical cashier/name/date and the exact close totals/boundary remain frozen. Active Owner/Manager can finalize a suspended/removed cashier's drawer with recorded override; opening and trading still require the active cashier. Overdue finalization preserves the original open period/date, requires manager/reason and an explicit mode; mode authorization occurs before replay. No cross-day trading or reopen exists.

Reads are `GET /api/tills`, `/capabilities`, `/sessions` and `/sessions/{id}`. Staff see their own sessions with financial parent references withheld; Owner/Manager see shop sessions and projected sale/payment/expense parent identities. Source return/cancellation IDs are not sale IDs. Till operation lookup `/api/tills/operations/{create|open|cash-movement|close|review}/{key}` restricts Staff to their own receipts. Generic financial lookup remains Owner/Manager and adds expense-record/correction and sale-return/void; payment reversal has no generic lookup slug and recovers by the same frozen-key retry on its original endpoint. Operations18–22 are till lifecycle,23–24 expense,25–26 return/void. Return POST retains current Sale DTO and void204; saved typed operation outcomes retain `{saleReturnId,saleId}` for recovery rather than a historical current-sale response snapshot. Committed authorized replay and reads survive close/disable.

Offline queue fields `Till`, `TenderLines`, `TillBusinessDate`, `TillCashierId` are frozen evidence. The shared poster either includes a valid original open same-day invoice normally or holds `TillReview=13`. Explicit Owner/Manager `ReconcileTillSale=6` posts the unchanged invoice and one immutable `TillLatePosting` excluded from original/current count. It refuses supplemental review for a valid current drawer. Verified-issuer TillReview ordinary retry requires all four fields (empty lines valid for zero-paid); it passes the same gate/period/session/interval/tender policies and cannot fall through to disabled legacy posting. Staff authorization permits only eligible own-case Escalate before replay. Legacy Unknown is visible, Recorded matches unambiguous original Cash/UPI/Card, and a positive-paid legacy Credit may use separately reasoned actual tender without mutating the envelope. Close totals, original invoice number/date and envelope are never rewritten by late reconciliation.

Recorded non-cash evidence is not a gateway/bank confirmation. Disable new work operationally only after resolving drawers/queue; retain schema, facts, readers and authorized replay. Pre-feature writers/binaries and destructive Down are unsafe after posting. No mobile, shared simultaneous drawer, mixed tender, automatic refund or bank reconciliation is included.
