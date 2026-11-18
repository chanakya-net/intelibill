# P03 full-payment acceptance

This gate covers Part 3 purchases, reconciliation, renewal and provider switching. Passing local checks does not authorize payment activation or enforcement. Money, licensing fulfilment and merchant acknowledgement are separate facts. A confirmation proves a verified receipt; it never proves applied access.

## Provenance and bounded scope

- **Development simulator:** real licensing and merchant PostgreSQL databases, fixed checkout/lifecycle clocks, server verification and signed HTTP outbox/inbox delivery. Manual receipts use a fictional fixture bank and statement policy.
- **In-process Razorpay HTTP fixture:** P0304 adapter/recovery contracts and P0305 webhook cases. Provider responses and signatures are controlled fixtures.
- **Playwright hosted-script stand-in:** P0304 Chromium launcher cases. The other browsers use the development simulator and real current services/builds.

None establishes real provider credentials, account activation, hosted checkout methods, receiving-account settlement or external webhook reachability. No live gateway was contacted, no charge was authorized, and no production configuration or schema is changed by P03-12.

“No partial activation” means no partial funding or partial licensing package: receipt, complete named-shop term, links, confirmation and publications commit atomically. Merchant projections can arrive one shop at a time; the package remains **Applying** until all required signed revisions are acknowledged.

## Scenario index

Each row uses the source links below and its R-command log under the evidence root. The final tested SHA, cwd, counts, exit codes and UTC boundaries are in the execution ledger. New composed tests supplement all predecessor gates.

| Behavior | Source and retained regressions | Evidence |
| --- | --- | --- |
| Frozen quote, stale quote, exact 15-minute boundary | [P0301 acceptance](../integration/P0301Tests.Acceptance.cs), [P0310 boundaries](../integration/P0310Tests.Boundaries.cs), [quote browser](p03-01-checkout.md) | R03/R05/R11; `UnknownOriginalAttemptAfterSwitchSettlesOnceWithOriginalConfirmation` accepts the original order before expiry and recovers after expiry |
| Full capture; callback/authorization is no proof; rollback of the complete package | [P0302 verification](../integration/P0302Tests.Verification.cs), [P0304 contracts](../integration/P0304Tests.Contract.cs), [P0312 interruption](../integration/P0312Tests.cs) | R03/R04/R05/R12/R14; `p0312/interruption-{rolled-back,retried,ack-lost,applied}.json` |
| Delayed capture | [SimulatorDelayedCaptureAndDefinitiveFailureRemainDistinct](../integration/P0303Tests.Simulator.cs), [ContractPendingCaptureGrantsNothingUntilTheFullCaptureIsObserved](../integration/P0304Tests.Contract.cs), [UnknownOriginalAttemptAfterSwitchSettlesOnceWithOriginalConfirmation](../integration/P0312Tests.Recovery.cs) | R03/R04/R05/R13/R14/R20; pending capture grants nothing, distinct from definitive failure; original A captures after quote expiry; `p0312/switch-{unknown,captured-applying,applied}.json`, R04/R05 logs and `p0304/captured-playwright.log` |
| Lost reply, lookup 404, exhausted retries, original key, no replacement charge | [P0303 leases](../integration/P0303Tests.Leases.cs), [recovery browser](p03-03-checkout-recovery.md), [P0312 recovery](../integration/P0312Tests.Recovery.cs) | R03/R04/R05/R13/R14/R20; `p0312/switch-unknown.json` records zero money/term/confirmation |
| Both manual/online race winners, two genuine receipts, one whole term | [P0312 deterministic races](../integration/P0312Tests.Races.cs), [manual receipts](p03-06-manual-payment.md), [P0311 issuance](../integration/P0311Tests.Issuance.cs) | R03/R05/R16/R17; `p0312/race-manual-{True,False}-{applying,applied}.json`; PostgreSQL fence/contender PIDs, two source receipts, two confirmations, one persisted provider session |
| Under/overpayment, two underpayments summing to price, wrong currency/account, duplicate capture | [P0307 captures](../integration/P0307Tests.Captures.cs), [review decisions](../integration/P0307Tests.Decisions.cs) | R05/R17; zero partial term or top-up; duplicate genuine funds remain a finance exception |
| Forged, duplicate/reordered webhooks and crash/lease recovery | [P0305 processing](../integration/P0305Tests.Processing.cs), [persistence](../integration/P0305Tests.Persistence.cs), [P0312 recovery](../integration/P0312Tests.Recovery.cs) | R03/R05/R15; authenticated original A event deduplicates while explicit verification races inbox processing |
| PaidNeedsReview after source/coverage changes | [P0307 recovery](../integration/P0307Tests.Recovery.cs), [restoration](../integration/P0307Tests.Restoration.cs), [review browser](p03-07-payment-review.md) | R05/R17; retain verified money and original order, no replacement payment |
| Early/grace renewal, late on-time proof, month-end/leap-day anchors, exact grace end, superseded predecessor | [P0312 delayed renewal](../integration/P0312Tests.Renewal.cs), [P0308 grace](../integration/P0308Tests.Grace.cs), [races](../integration/P0308Tests.Races.cs), [P0304 renewal](../integration/P0304Tests.Renewal.cs) | R03/R04/R05/R18; `p0312/renewal-grace-{True,False}-{applying,applied}.json`; creation-only Razorpay time is not capture proof |
| First purchase, trial conversion, restored-access start without restricted-gap charge, mixed package and suspension | [P0309 scenarios](../integration/P0309Tests.cs), [source evidence](../integration/P0309Tests.SourceEvidence.cs), [adjustments](../integration/P0309Tests.Adjustment.cs) | R05/R19; [fresh-start guide](p03-09-fresh-start.md) |
| Future provider B; historical A still recoverable; absent/rebound configuration refuses fallback | [P0310 configuration retention](../integration/P0310Tests.ConfigurationRetention.cs), [workers](../integration/P0310Tests.Workers.cs), [P0312 recovery](../integration/P0312Tests.Recovery.cs) | R03/R05/R20; original A reference and four-part pin; four 404 lookups on A including two after switch; new-key refusal; GatewayProbe asserts one original create plus one disjoint B create and no further creates through acknowledgement |
| Immutable private confirmations and payment/access distinction | [P0311 guide](p03-11-payment-confirmation.md), [P0310 composed browser](../browser/p03-10-provider-switch.spec.ts), [C# checkpoints](../integration/P0310BrowserTests.Confirmation.cs) | R03/R05/R20/R21; four identical merchant/admin attachments across Applying/Applied, no-store/attachment/nosniff, desktop/mobile screenshots |
| Simulator/Razorpay contract parity: capture, cancellation, pending, malformed proof, pin/amount/currency mismatch and unsupported methods | [P0304 contracts](../integration/P0304Tests.Contract.cs), [pins](../integration/P0304Tests.Pins.cs), [fixture guide](p03-04-razorpay-purchase.md) | R04/R14; both provider datasets are required, never live Razorpay certification |

## Observable local demo

R20 runs the existing real-service harness and shipping merchant/admin builds. It loses the browser reply after original A checkout creation, switches future orders to B through owner/MFA/CSRF authorization, then inspects A separately from current B. A missing historical profile blocks verification without replacement checkout across reload, a second tab and account changes. Restoring A and delivering the same authenticated capture twice yields one verified receipt/term/confirmation.

Before releasing delivery, the merchant shows payment confirmed and **Applying**, and both merchant and Billing operator download the same A-pinned confirmation. The C# checkpoint asserts zero acknowledgement. After signed delivery, it verifies the merchant's persisted revision and signed receipt, the UI shows **Applied**, and all four downloads remain byte-identical. `p0310/confirmation-{applying,applied}.json`, `confirmation-observations.json`, `browser-postgres.json`, `safe-network-counts.json`, the attachment files and desktop/mobile PNGs preserve the observations. No credentials or session headers belong in this evidence.

## Reproduce the finite gate

Use the repository SDK pin (`global.json`, .NET 10 with latestMinor roll-forward), installed Bun lockfile dependencies, Docker and repository Playwright Chromium. An unavailable prerequisite, zero discovery or skipped required test is a failed/blocked gate. Keep browser checks sequential and run R09/R10 before R11–R21. Both APIs use separate real PostgreSQL databases provisioned by the harness; no SQLite replacement.

From the assigned worktree, set an **absolute external** evidence directory, and precreate every output directory:

```bash
export P0312_EVIDENCE_ROOT="${P0312_EVIDENCE_ROOT:-${TMPDIR:-/tmp}/intelibill-p03-evidence/final-$(git rev-parse HEAD)}"
export P0312_SOURCE_SHA="$(git rev-parse HEAD)"
mkdir -p "$P0312_EVIDENCE_ROOT"/{p0301,p0302,p0303,p0304,p0305,p0306,p0307,p0308,p0309,p0310,p0311,p0312}
```

For each literal command below, capture cwd, `git rev-parse HEAD`, UTC start/end, combined stdout/stderr, exit status and actual discovered/passed/failed/skipped counts outside source. Preserve nonzero runs separately. R09/R10 deliberately include repository-root `cd` so check identities are distinct; R07 and R08 run in the specified app directories. R03 must execute six cases; R11–R21 must execute 17 outer methods (P0304 has seven; the other ten have one), zero skipped, with each child Playwright process complete and passing. Build hashes must come from the newly built output, not earlier evidence.

| ID | Cwd | Literal command |
| --- | --- | --- |
| R01 | `repository root` | `dotnet build src/backend/Intelibill.slnx` |
| R02 | `repository root` | `dotnet build src/licensing/Intelibill.Licensing.slnx` |
| R03 | `repository root` | `P0312_EVIDENCE_DIR="$P0312_EVIDENCE_ROOT/p0312" dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0312Tests' --logger 'console;verbosity=normal'` |
| R04 | `repository root` | `dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0304Tests' --logger 'console;verbosity=normal'` |
| R05 | `repository root` | `P0305_EVIDENCE_DIR="$P0312_EVIDENCE_ROOT/p0305" dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0301Tests|FullyQualifiedName~P0302Tests|FullyQualifiedName~P0303Tests|FullyQualifiedName~P0305Tests|FullyQualifiedName~P0306Tests|FullyQualifiedName~P0307Tests|FullyQualifiedName~P0308Tests|FullyQualifiedName~P0309Tests|FullyQualifiedName~P0310Tests|FullyQualifiedName~P0311Tests' --logger 'console;verbosity=normal'` |
| R06 | `repository root` | `dotnet test tests/backend/unit/Intelibill.Api.Unit.Tests/Intelibill.Api.Unit.Tests.csproj --filter 'FullyQualifiedName~MerchantCheckoutPaymentRelayTests' --logger 'console;verbosity=normal'` |
| R07 | `src/frontend` | `bun run test --run src/app/features/billing` |
| R08 | `src/admin-frontend` | `bun run test --watch=false` |
| R09 | `repository root` | `cd src/frontend && bun run build` |
| R10 | `repository root` | `cd src/admin-frontend && bun run build` |
| R11 | `repository root` | `P0301_RUN_BROWSER=1 P0301_EVIDENCE_DIR="$P0312_EVIDENCE_ROOT/p0301" dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0301BrowserTests' --logger 'console;verbosity=normal'` |
| R12 | `repository root` | `P0302_RUN_BROWSER=1 P0302_EVIDENCE_DIR="$P0312_EVIDENCE_ROOT/p0302" dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0302BrowserTests' --logger 'console;verbosity=normal'` |
| R13 | `repository root` | `P0303_RUN_BROWSER=1 P0303_EVIDENCE_DIR="$P0312_EVIDENCE_ROOT/p0303" dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0303BrowserTests' --logger 'console;verbosity=normal'` |
| R14 | `repository root` | `P0304_RUN_BROWSER=1 P0304_EVIDENCE_DIR="$P0312_EVIDENCE_ROOT/p0304" dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0304BrowserTests' --logger 'console;verbosity=normal'` |
| R15 | `repository root` | `P0305_RUN_BROWSER=1 P0305_EVIDENCE_DIR="$P0312_EVIDENCE_ROOT/p0305" dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0305BrowserTests' --logger 'console;verbosity=normal'` |
| R16 | `repository root` | `P0306_RUN_BROWSER=1 P0306_EVIDENCE_DIR="$P0312_EVIDENCE_ROOT/p0306" dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0306BrowserTests' --logger 'console;verbosity=normal'` |
| R17 | `repository root` | `P0307_RUN_BROWSER=1 P0307_EVIDENCE_DIR="$P0312_EVIDENCE_ROOT/p0307" dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0307BrowserTests' --logger 'console;verbosity=normal'` |
| R18 | `repository root` | `P0308_RUN_BROWSER=1 P0308_EVIDENCE_DIR="$P0312_EVIDENCE_ROOT/p0308" dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0308BrowserTests' --logger 'console;verbosity=normal'` |
| R19 | `repository root` | `P0309_RUN_BROWSER=1 P0309_EVIDENCE_DIR="$P0312_EVIDENCE_ROOT/p0309" dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0309BrowserTests' --logger 'console;verbosity=normal'` |
| R20 | `repository root` | `P0310_RUN_BROWSER=1 P0310_EVIDENCE_DIR="$P0312_EVIDENCE_ROOT/p0310" dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0310BrowserTests' --logger 'console;verbosity=normal'` |
| R21 | `repository root` | `P0311_RUN_BROWSER=1 P0311_EVIDENCE_DIR="$P0312_EVIDENCE_ROOT/p0311" dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0311BrowserTests' --logger 'console;verbosity=normal'` |

## Execution ledger

The pre-check completed R01–R21 with no failures, zero skipped browser methods and no zero-test discovery. This was uncommitted acceptance work over baseline `8904520f2e532d2804bc2ae1253bf60ae9048124`, **not** committed-HEAD proof. Its timed logs are in `precommit-8904520f2e532d2804bc2ae1253bf60ae9048124/`; the latest strengthened six-case run is in `targeted-current-8904520f2e532d2804bc2ae1253bf60ae9048124/`. Initial exploratory outputs are labeled `first-acceptance-uncommitted-base-...` and `exploratory-exits.json`.

| Checks | Observed pre-check result |
| --- | --- |
| R01/R02 | Both builds passed; 0 warnings, 0 errors |
| R03 | 6 passed, 0 failed, 0 skipped |
| R04 | 121 passed, both simulator and Razorpay fixture datasets |
| R05 | 148 passed; P0301=11, P0302=16, P0303=6, P0305=19, P0306=10, P0307=15, P0308=13, P0309=11, P0310=31, P0311=16 |
| R06 | 18 merchant relay tests passed |
| R07 | 186 tests / 25 files passed |
| R08 | 124 tests / 21 files passed |
| R09/R10 | Both current shipping builds passed; existing merchant style-budget warnings retained |
| R11–R21 | 17 outer browser methods passed, 0 skipped; R14 executes seven and each other class one |

The final gate must be run again against the committed HEAD; its authoritative source SHA and one result per exact command belong in the native implementation handoff, `workers/impl/cycle-1-attempt-1-result.json`, under the issue artifact directory. That handoff binds `evidence/final-<commit-sha>/R01.json` through `R21.json` and matching logs, plus `toolchain.json` and `build-hashes.json`. Each command metadata file records its actual cwd, SHA, UTC boundaries, exit and counts; the build manifest records current shipping SHA-256 hashes. Every scenario row above inherits that tested SHA and its listed R-log/evidence paths. The Markdown deliberately does not embed its own self-referential commit hash or random fixture receipt IDs. Use the handoff and timed command metadata to establish final acceptance, not this pre-check table. Final screenshots and attachment hashes must be newly generated in that final directory.

The pre-check used system Bun 1.3.14. The repository-pinned Bun 1.3.11 was staged outside source for the final gate; its frozen install made no dependency/lockfile changes. No global runtime was replaced. Raw evidence stays outside source and is neither a reusable credential nor launch approval.

Unchanged baseline `8904520f2e532d2804bc2ae1253bf60ae9048124`: the initial ambient PATH selected .NET 11 and packages were absent (`baseline-...` logs). Using existing `/usr/local/share/dotnet` (.NET 10.0.300, allowed by latestMinor) and `bun install --frozen-lockfile` restored prerequisites. `baseline-ready-...` preserves the resulting CA1825 compiler failure at `P0304Tests.Contract.cs:12`, which prevented licensing/browser execution. The focused correction explicitly supplies `Array.Empty<string>()` to the theory dataset constructor and keeps both datasets/assertions. Merchant build, 18 relay tests, 186 merchant billing tests, 124 admin tests and both web builds passed at that baseline; existing web style-budget warnings are retained.

First-execution classification: slice 1 and slice 3 passed existing behavior. Slice 2's initial failed assertion was test setup: a lowercase SQL email matched no normalized uppercase identity, so deactivation did nothing. The test now targets the normalized identity and asserts exactly one affected identity before replay. All six composed acceptance cases then passed without payment-production changes. A first compilation also caught CA1869 in the new evidence serializer; it was corrected to reuse the existing options. These are not missing-payment-behavior red evidence. Publication/acknowledgement fault injection demonstrates resilience; it does not imply production was broken.

## Human launch inputs — not executed

Authorized provider test-account activation and configuration; test/live credential and webhook-secret delivery through secret management; supported full-payment checkout methods and disabled unsupported choices; receiving-account identity/settlement evidence; externally reachable authenticated webhook; retained historical adapter/key/configuration availability; and approved manual receiving-account/reference/statement verification rules remain separate human inputs. Fixture bank receipts do not establish those rules operationally. Retain P03-11's [quiesced writer cutover and zero-missing-confirmation gate](../../../docs/licensing-rollout.md#p03-11--immutable-payment-confirmations). No local result authorizes production payment, enforcement, refund processing or deployment.
