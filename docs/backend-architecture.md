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
Adopted so far: customer payments (`customer-payment`) and supplier payments
(`supplier-payment`). Sales keep their own `(shop, actor, idempotencyKey)` deduplication.

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
transaction that first takes the sale's customer row (when the sale has one) and then the sale row,
each with `SELECT … FOR NO KEY UPDATE` scoped by `shop_id` (`ISaleRepository.LockForReturnMutationAsync`),
then validates and writes, and commits only on success. The customer lock is there because a due
reduction is capped by the customer's balance across all their sales, so returns on two sales of one
customer must queue too. Validation runs under the lock, so a competing return that committed first is counted and
the loser answers 400 `SaleReturn.QuantityExceedsRemaining` (or `SaleReturn.AlreadyVoided`); the
preview stays advisory. `NO KEY UPDATE` keeps inserts that only reference the sale (their FK key-share
check) from queueing behind it. The void reads only the return's immutable sale id before the lock
(`ISaleReturnRepository.GetSaleIdAsync`) and everything it decides on after.

**Order.** customer → sale → that sale's credit notes (`GetBySaleReturnIdForUpdateWithRedemptionsAsync`) →
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
a save barrier rather than sleeps. Customer payments do not take the customer lock yet (plan 25), so a
payment racing a due-reducing return can still leave the balance below zero. Returns carry no
operation key yet, so a resend after a lost response is a new return and is bounded only by
entitlement.

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
post, after only the id, invoice-number and NUL-character checks (a NUL in any text field answers
per-sale `Sale.OfflineTextInvalid`). The row holds the device, `clientSaleId`, invoice number, the
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
envelope (`FindOpenIssueIdAsync(..., offlineSubmissionId)`).

**Issuer and device history.** New queued payloads carry `issuedByUserId` (hashed only when present).
Absent = legacy (attributed to the submitter, issuer "not recorded"); equal to the submitter =
verified; different = held (`IssuerNotSubmitter`). `invoice_leases.reserved_by_user_id/session_id`
record who reserved a range. For a recovery row an asserted issuer is verified for a device when they
hold or held a membership (any status) and reserved a lease on it or synced from it. Never a member:
row rejected (`Recovery.IssuerMismatch`). Member without device history, or no issuer: envelope kept,
case escalated (`IssuerUnverified` / `ProvenanceUnknown`), nothing posted. People are named only
through shop membership; a non-member renders with an empty `displayName`.

**Actions** (`ReconciliationActionPolicy.AvailableActions`; owner/manager, staff get only `Escalate` on
Open cases they created; resolved cases get none; `Escalate` only from Open):

| Case | Actions |
|---|---|
| Pricing / discount / customer variance | `AcknowledgeVariance`, `Escalate` |
| Stock variance with item, batch and printed > consumed | `LinkStockCorrection`, `Escalate` |
| Envelope + `HistoricalLeaseReviewRequired` / `RecoveryReviewRequired` / `IssuerNotSubmitter` | `RetryHistoricalLease` (needs an attributable user), `LinkExistingSale`, `Escalate` |
| Envelope + `Sale.IdempotencyConflict` / `Sale.InvoiceNumberAlreadyUsed` | `LinkExistingSale`, `Escalate` |
| Anything else (arithmetic, lease not found, other timing codes, legacy held cases, `ProvenanceUnknown`, `IssuerUnverified`) | `Escalate` |

`LinkStockCorrection` needs an unvoided same-shop/item/batch `FoundStock` or `OtherGain` increase worth
at least the unresolved quantity, not linked elsewhere (partial unique index); it writes one `Out`
movement and leaves the sale untouched. `LinkExistingSale` needs a sale with the envelope's device,
client id and hash. `RetryHistoricalLease` links an identical existing sale, refuses a reused invoice
number, or posts through `IOfflineSalePoster` and commits sale, envelope link and resolution together;
a held outcome answers 409 `Reconciliation.ReplayRejected` and changes nothing. Every transition appends
a `reconciliation_case_events` row. There is no edit-price, renumber or delete action.

**Concurrency and idempotency.** Resolve and recovery run through `IFinancialOperationExecutor`
(types 7 `ReconciliationResolution`, 8 `OfflineRecoverySubmission`): a replay returns the stored
response, a changed payload under the same key answers 409 `Operation.PayloadMismatch`. Resolve locks
the case `FOR NO KEY UPDATE`; a stale `expectedVersion` (`xmin`) or a concurrent commit answers 409
`Reconciliation.CaseVersionConflict`. Another shop's case is invisible under RLS (404). The list
clamps page to at least 1 and pageSize to 1-100.

**Recovery bundles.** `POST recovery-submissions` (active owner/manager) takes
`{format: "intelibill.offline-recovery", version: 1, shopId, checksum, invoices[]}`: 1-100 invoices,
request at most 2 MiB (Kestrel answers 413). `checksum` is the lowercase hex SHA-256 of the exact UTF-8
text of the `invoices` array (client `JSON.stringify`, server `JsonElement.GetRawText()`); enum names
or numbers are accepted. Bundles hold no tokens or session data. Structure and arithmetic are checked
before any case is filed; every bundle then opens a review (`RecoveryReviewRequired`) unless an
identical sale already exists (checked first; the envelope is linked and nothing else happens). The
cashier's access is never restored.

**Rollout and rollback.** Migration `AddOfflineSubmissionReconciliationAudit` only adds tables and
columns; existing cases take `status` from `is_resolved`, have no envelope ("original envelope not
recorded") and legacy held cases allow `Escalate` only. Rollback removes the routes and menu entry;
envelopes, cases, events and leases stay and invoice ranges are never recycled.

**Out of scope.** A device-revocation registry, closed accounting periods (backlog 27), offline
attestation of the issue time, and reprint polish (backlog 21).

## Test Structure

| Suite | Path | Count |
|---|---|---|
| Domain unit | `tests/backend/unit/Intelibill.Domain.Unit.Tests/` | 296 |
| Application unit | `tests/backend/unit/Intelibill.Application.Unit.Tests/` | 1424 |
| API unit | `tests/backend/unit/Intelibill.Api.Unit.Tests/` | 557 |
| Integration | `tests/backend/integration/Intelibill.Integration.Tests/` | 605 |
| **Total** | | **2882** |

Integration tests use `WebApplicationFactory<Program>` backed by **Testcontainers** (`Testcontainers.PostgreSql`) — Docker required. Non-integration suite: 2277 passing.
