# API Cold-Start Reliability Design

**Date:** 2026-07-28
**Status:** Approved
**Scope:** Development API only for resource sizing; shared API image for startup optimizations

## Problem

The deployed development API intermittently returns `503` for the first write
request, completes the next request slowly, and then behaves normally.

Runtime evidence shows the API exceeding its 1 GiB container limit and being
killed with exit code 137. The replacement process then performs Wolverine
handler compilation (including Roslyn) and Entity Framework model construction
on the first business request. That produces the observed sequence:

1. the original request reaches a dying/restarting replica and receives `503`;
2. the next request pays one-time runtime compilation and model initialization;
3. subsequent requests use the warmed process and complete normally.

## Decisions

### Scale-to-zero remains enabled

The API will use `min_replicas = 0` in every environment. This is an explicit
user requirement. It preserves zero idle compute cost, but means the first
request after an idle period can still wait for Azure Container Apps to start a
replica. This design makes that cold start reliable and bounded; it does not
claim to eliminate platform scale-from-zero latency.

### Development API capacity becomes 1 vCPU / 2 GiB

Only the development API changes from 0.5 vCPU / 1 GiB to 1 vCPU / 2 GiB.
Production remains at 0.75 vCPU / 1.5 GiB and is not being tested or resized by
this change.

The larger development allocation provides headroom above the observed memory
high-water mark and reduces CPU contention during process startup.

### Wolverine code is generated while building the image

Production will use Wolverine's static generated-code loading mode and assert
that required generated types exist. The Docker build will run Wolverine's code
generation command before `dotnet publish`, so handler plumbing is compiled into
the image instead of invoking Roslyn on the first production request.

Development and test execution retain dynamic generation for the normal local
developer experience.

### Entity Framework model is initialized before readiness

A one-time hosted startup service will resolve `ApplicationDbContext` and access
its model before the web host reports ready. The warm-up is read-only: it will
not migrate the database or write application data. Startup will fail rather
than advertise readiness if model construction fails.

The existing readiness database check remains responsible for database
connectivity. Keeping the responsibilities separate avoids schema mutation and
does not turn a recurring health probe into a costly warm-up operation.

## Cost in INR

Using the public Azure Container Apps Consumption prices returned for Central
India on 2026-07-28:

| Configuration | Active replica-hour |
|---|---:|
| Previous baseline: 0.25 vCPU / 0.5 GiB | about ₹2.61 |
| New development API: 1 vCPU / 2 GiB | about ₹10.44 |
| Increase while active | about ₹7.83 |
| Scaled to zero | ₹0 compute |

Request charges and free grants are unchanged. Actual billed amounts can differ
with agreement pricing, free monthly grants, and the number of active hours.

## Error Handling and Observability

- Static generation is validated at build time and asserted at production
  startup, so a missing generated handler fails visibly instead of falling back
  to runtime compilation.
- EF warm-up failures use normal host startup failure handling and structured
  logs; the replica never becomes ready in a partially initialized state.
- No automatic retry is added to create-item, add-user, or create-supplier
  requests because retrying writes at the UI boundary could duplicate effects.

## Verification

- OpenTofu contract tests assert development API sizing and scale-to-zero.
- API unit tests verify the startup warm-up registration and its read-only
  behavior.
- A production Docker build proves generated Wolverine source is created before
  publish and accepted by static loading.
- The backend solution build and test suites guard existing behavior.

## Out of Scope

- Production resource changes or production deployment testing.
- Keeping an always-warm replica.
- Database migrations during API startup.
- Generic client-side retries for write operations.
