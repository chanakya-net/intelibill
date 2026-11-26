# P06-14 Commercial Management Acceptance — Changes, Transfers, Refunds and Documents

This record closes slice **P06-14** ([#923](https://github.com/chanakya-net/intelibill/issues/923)) under parent PRD [#828](https://github.com/chanakya-net/intelibill/issues/828) (InteliBill Licensing P06 — Subscription Changes, Refunds and Billing Documents, user stories 1–11). It composes the merged P06 slices (#909–#922) on shared shop, term and receipt identities and records what is proven, what remains a known prerequisite gap, and what is still a human input.

## Environment, Baseline and Evidence Boundaries

- **Parent baseline SHA**: `e22bf98f66a3002da5b832f2af9fc2c5862420b5` (includes the #916 and #922 merges and every earlier P06 slice).
- **New test sources**: `tests/licensing/integration/P0614Tests.cs`, `P0614Tests.Refunds.cs`, `P0614Tests.Transfer.cs` (one partial class, three composed facts). No product, API, migration, dependency or configuration file changes.
- **Verification toolchain**: `.NET SDK 10.0.300` from `/usr/local/share/dotnet` (satisfies `global.json` `10.0.105`, `latestMinor`); `Bun 1.3.11` (repository pin; `bun install --frozen-lockfile`); Playwright `1.62.0` Chromium from the lockfile; PostgreSQL `postgres:17-alpine` through Testcontainers on Docker `29.8.2` (OrbStack).
- **Clocks**: each composed fact uses its own fixed business instants, separate from the admin auth/MFA clock, the signed-transport clock and the refund lease clock:
  - Fact 1: `2028-01-31T04:30Z` purchase, `2028-02-14T04:30Z` change, `2028-02-29T04:30Z` paid-through.
  - Fact 2: `2028-01-31T04:30Z` purchase/request/approval, `2028-01-31T04:31Z` recovery (clock +1 min).
  - Fact 3: `2028-01-31T04:30Z` purchase, `2028-02-14T10:00Z` early-start (`P0606Tests.EarlyStart`), `2028-02-16T10:00Z` permit `ValidUntil` cutover.
- **Attribution**: all money movement is the local development payment simulator (profiles `simulator-a` / `simulator-b`, environment `Development`). Provider timeouts, the delayed `Processed` result and the profile switch are explicit simulator controls (`POST …/refund-requests/{id}/simulator`, `PaymentProfiles` selection) — fixture faults, not live provider observations. No live Razorpay or bank endpoint was contacted, no credentials or production flags were enabled, and nothing was deployed or published.
- **Evidence location**: measured command output, browser captures and the PR handoff live outside the repository under `.run-with-it/issues/923/evidence/<commit>/<attempt>/` (`pr-handoff.md`, `summary.md`). This record pins the invariants and commands; identifiers and timestamps are per-run values.

## Prerequisite Setup

Run from the repository root:

```bash
export DOTNET_ROOT=/usr/local/share/dotnet
export PATH="<bun-1.3.11-dir>:$DOTNET_ROOT:$PATH"
test "$(bun --version)" = 1.3.11
bun install --frozen-lockfile
(cd src/admin-frontend && bun x playwright install chromium)   # only if Chromium is missing
export P0614_EVIDENCE="$(mktemp -d /tmp/intelibill-p0614.XXXXXX)"
dotnet build src/licensing/Intelibill.Licensing.slnx -c Release
dotnet build src/backend/Intelibill.slnx -c Release
(cd src/frontend && bun run build)
(cd src/admin-frontend && bun run build)
```

Run every `scripts/dev/test-integration.sh` invocation **sequentially** (each one generates and deletes `src/backend/Intelibill.Api/Internal/Generated`) with `INTEGRATION_TEST_PARALLELISM=12`.

## Behavior-First Control

All three composed facts passed on the untouched merged product (baseline GREEN), so the invariant was demonstrated with narrowly controlled negative controls on an uncommitted working copy, then restored. Both controls are **simulated**; the committed sources contain neither change.

| Fact | Controlled change (temporary, never committed) | Observed failure | Restored result |
| --- | --- | --- | --- |
| `FinalSlotReceiptInvalidatesEarlierReductionSelection` | Removed the signed slot-receipt drain (`DrainSlotAsync`) and its applied-state check before the stale reduction submission, so the old two-shop preview is submitted while the third shop's slot is still undelivered. | `Assert.Equal() Failure: Expected: Conflict, Actual: Created` — the stale selection schedules (`201`) when the receipt has not yet landed. | Passed (`1/1`), stale submission `409 Billing.CapacityReductionSourceChanged`. |
| `UnknownRefundKeepsReceiptBoundAcrossSwitchAndContention` | Replaced the delayed `Processed` simulator control on the held reservation with another `Timeout`. On the recovery read the simulator maps `Control = Timeout` to `ObservedRefundState.Pending` (`DevelopmentPaymentGateway.Refunds.cs`), so the original provider reports the refund as still pending (unconfirmed — not the P06-10 `Unknown` state, which only a `Mismatch`/rejected read produces) when confirmation is expected. | `Assert.Equal() Failure: Expected: "Confirmed", Actual: "Pending"` — the customer status never claims a refund the original provider did not confirm. | Passed (`1/1`). |

Commands (each used for both its controlled and restored runs):

```bash
INTEGRATION_TEST_PARALLELISM=12 scripts/dev/test-integration.sh --licensing --filter 'FullyQualifiedName~P0614Tests.FinalSlotReceiptInvalidatesEarlierReductionSelection' --logger 'console;verbosity=normal'
INTEGRATION_TEST_PARALLELISM=12 scripts/dev/test-integration.sh --licensing --filter 'FullyQualifiedName~P0614Tests.UnknownRefundKeepsReceiptBoundAcrossSwitchAndContention' --logger 'console;verbosity=normal'
```

## Composed Scenario Evidence (new)

### 1. Final paid slot changes the owner's reduction choices — stories 2, 3, 4

`P0614Tests.FinalSlotReceiptInvalidatesEarlierReductionSelection` (`P0604Tests.PrepareAsync` on `P0308Fixture`). Account with four affiliated shops (Central = payer, East, North, South), a paid Multi-shop month term `2028-01-31T04:30Z → 2028-02-29T04:30Z` with capacity 3 and two linked shops; a real Staff member is admitted to Central before any change.

1. Owner previews a reduction to a two-slot package (`9,000` paise) at `2028-02-14T04:30Z`: `sourceShopCount = 2`.
2. North's billing-owner request → shop-owner grant/accept → signed consent receipt → `coverage_slot_assignments.state = Applied`, exactly one assignment, three `payment_fulfilment_shops` rows on the one term, North `paidThrough = 2028-02-29T04:30Z` (shared end; no new fee or term).
3. The stale preview with the two originally valid retained shops (Central, East) is refused: `409 Billing.CapacityReductionSourceChanged`; zero `scheduled_capacity_reductions`, unchanged `checkout_orders` count.
4. Exact redelivery of North's signed consent receipt answers `204` and changes nothing (one assignment, three links, one term).
5. A fresh preview names all three shops (`sourceShopCount = 3`, `minimumShopsToRemove = 1`, every `retained = null`); the owner explicitly retains East + North. The reduction order is accepted and fully paid once through the default local simulator (`P0302Fixture.SimulatorSettings`, profile `local-simulator`) (`Funded`).
6. At `2028-02-29T04:30Z − 1µs` the schedule stays `Funded`; at exactly `2028-02-29T04:30Z` it moves to **`NeedsReview / SourceChanged`** — see known gap **G1**. No successor term, no `applied_shop_reductions`, no shop `Expired`, one `verified_payments` row and zero `payment_fulfilments` for the reduction order, one finance `payment-reviews` case classified `SourceChanged` for that order.
7. Every `shop_memberships` row of the four shops and of the payer is byte-for-byte identical before and after (including the Staff member in omitted Central); the payer holds no membership in East/North/South.

### 2. Unknown refund keeps the receipt bound across a profile switch and contention — stories 7, 8, 9

`P0614Tests.UnknownRefundKeepsReceiptBoundAcrossSwitchAndContention` (`P0609Tests.StartAsync` with `P0310Tests.Settings()`). One shop, one verified `12,000`-paise purchase captured under simulator A; three customer requests on the same confirmation (`6,000`, `5,000`, `4,000`).

| Step | Actor / route | Native progression and evidence |
| --- | --- | --- |
| Approve | Billing operator α, fresh MFA, `POST …/approvals` | `4,500` with `Preserve`: `201`, reservation held. |
| Timeout | simulator control `Timeout`, refund worker pump | provider state `Unknown`, one `refund_sends`, zero `refund_applications`; customer list `Unknown`. |
| Switch | owner selects profile `b` (revision 1) | new attempts would pin B; nothing in flight moves. |
| Previews | α on `5,000`, β (second operator, own session) on `4,000` | both show `occupied = 4,500`, `remaining = 7,500`. |
| Contention | `HoldReceiptAsync` (`lock_refund_receipt`) + PostgreSQL `pg_blocking_pids` wait | exactly one `201`, one `409`; loser's refreshed preview `409 Refund.AmountExceedsRemaining` (either contender may win). |
| Recovery | simulator control `Processed`, clock +1 min, drain (exactly two claims: held read + winner send) then one post-settlement idempotency pump | idempotency pump claims nothing and adds no `refund_sends`, `refund_observations`, `refund_bindings`, `refund_applications` or simulator refund rows; held reservation: one send, exactly one `refund_observations` row `Confirmed`/`OriginalProviderRead`, one `refund_bindings` row pinned to `development-simulator`/`simulator-a`, `refund_work = Confirmed`, one `refund_applications` `Applied`; customer status `Confirmed`; operator recovery `GET …/approvals/operations/{key}` returns the same `reservationId`. |
| Pins | SQL on `refund_attempts`, `development_gateway_refunds` | attempt pinned to `development-simulator` / `simulator-a` / `Development` / `a/1`; one A refund of `4,500`; zero B refunds and zero B-pinned attempts. |
| Cap | SQL distinct reservations + refreshed preview | occupied = `4,500 + winner`, each reservation counted once across held/confirmed, ≤ `12,000`; one paisa above the exact remainder refused; service `paidThrough` unchanged (`Preserve`). |

### 3. Financial history stays with the payer through cutover — stories 6, 7, 8, 10, 11

`P0614Tests.TransferredShopKeepsOriginalRefundAndCorrectiveDocumentsPrivate` (`P0607aTests.StartAsync` with an RSA key). Source account (paid three-shop term, North trading with one real offline permit) and an unrelated destination account with its own independently dated month term.

1. Finance attaches v1 to the source order; the source payer requests a `3,000`-paise refund on that order's confirmation **before** the transfer. Snapshots: confirmation bytes, funding facts (`verified_payments`/`payment_fulfilments`/`payment_confirmations` for the order) and the destination term row.
2. `P0607Tests.ApplyTransferAsync` — three live consents (source billing owner, destination billing owner, North shop owner), authorized licensing-manager approval, signed prepare/consume/activate at `Permit.ValidUntil`. Merchant `shop_license_states.account_id` for North = destination account; one `coverage_transfer_bindings` row.
3. A distinct Billing operator (enrolled for this stage) approves the pre-transfer request with `Preserve` under fresh MFA after cutover; simulator `Processed`; one `refund_applications` `Applied`; one `refund_attempts` row on the source account and original order.
4. The same operator supersedes v1 with a distinct v2 (`supersedes_version_id = v1`).
5. Source payer: refund list shows exactly that request `Confirmed` on the original order; v1 and v2 download `200`, `Cache-Control: no-store`, `Content-Disposition: attachment`, bytes and SHA-256 equal the stored `content_sha256`, the two hashes differ; confirmation bytes and funding facts unchanged.
6. Destination payer: own refund list excludes the request; source refund list `403`; every source document/confirmation route `403`; the same routes with the destination account substituted `404`; v2 `403` / guessed `404`.
7. Destination term row unchanged, no fulfilment extends it, neither payer gains a North membership.

## Story Coverage Matrix

Row labels: **new composed** (this slice), **existing slice** (rerun on the committed head), **human input** (not provable from fixtures).

| Story | Evidence | Label |
| --- | --- | --- |
| 1 · prorated increase | `P0601Tests.ProrationV1VectorsMatchPolicy` (`tests/contracts/licensing/proration-v1.json`: zero, below-minimum, half-up, exclusive end), `OwnerQuotesLeapMonthIncreaseFromPurchasedGross` (12,000 → 33,000 paise over 2,505,600 s with 1,296,000 s remaining → **10,862** paise, Jan 31–Feb 29 2028, anchor preserved), `EligibilityOffersOnlyTheApplicableNextSteps` (trial/grace/expired), `TrialUpgradeConvertsThroughAFullPaidPurchase`, `P0602Tests.FullPaymentQueuesOneIncreaseWithoutCreatingATerm`, `ReservationDecisionSurvivesReplayAndKeepsOriginalAnchor` | existing slice |
| 2 · reduction with selected shops | `P0603Tests.AdmissionAndReductionConsumeSerializeAtTheFenceAndRecoverWithOriginalFunds`, `P0603BrowserTests` (applied + drift-resolved); composed fact 1 (stale selection refused, fresh explicit selection) | existing slice + new composed (gap **G1** open for slot-assigned terms) |
| 3 · resolve members, no silent removal | `P0603Tests` member conflicts, merchant `P0411LicensingTests.AcceptanceAndReinstatementRaceForTheFinalSeat`; composed fact 1 roster digest | existing slice + new composed |
| 4 · assign unused paid slot | `P0604Tests.FinalSlotReceiptsCompeteForOneRemainingTermAndGrantNoShopMembership`; composed fact 1 | existing slice + new composed |
| 5 · early replacement with permits | `P0605Tests.PermitReportedAfterSchedulingDefersSuccessorUntilExactExpiry`, `P0606Tests.CourtesyBridgeConservesThePurchasedSlotAtEveryBoundary`, `P0606BrowserTests` | existing slice |
| 6 · consented transfer | `P0607aTests.LiveReviewCheckRejectsLostAndRegainedAuthority`, `P0607Tests.AgreedIntervalsConserveOneFundedSlot`, `ApplyingTransferSerializesWithCompetingMutation`, `P0607BrowserTests`; composed fact 3 | existing slice + new composed |
| 7 · request and see status | `P0608Tests.MemberRequestsRefundWithoutChangingTermAccessBalanceOrShopStatus`, `RefundRequestContractPinsHashStatesBoundsAndMigrationAction`; composed facts 2 and 3 | existing slice + new composed |
| 8 · approve with reason and access | `P0609Tests.MfaOlderThanFiveMinutesCannotPreviewApproveOrRecover`, `ConfirmedPlusHeldCountsEachReservationOnce`; composed facts 2 and 3 | existing slice + new composed |
| 9 · original-provider recovery | `P0610Tests.InterveningPaidCapacityIncreaseMovesDelayedRefundAccessToNeedsReview`, `LostSendResponseAndLeaseExpiryNeverCauseAnotherPost`, `P0610BrowserTests`; composed fact 2 | existing slice + new composed |
| 10 · accounting invoice separate from confirmation | `P0611Tests.InvalidOversizedAndNonPdfContentIsRejected`, `ExactlyTenMiBAndTheNextIstDayAreAcceptedAtTheirBoundaries`, `P0612Tests.ContractFixtureMatchesSharedConstantsAndNoInlinePdfSurfaceExists`, `MemberListsOnlyTruthfulInvoiceMetadataForTheirOrder` (order-level empty history); composed fact 3 | existing slice + new composed |
| 11 · preserved correction version | `P0613Tests.ConcurrentCorrectionsConvergeOnOneSuccessor`, `P0613BrowserTests`; composed fact 3 | existing slice + new composed |
| Refund/transfer policy, issuer identity, invoices | see Human Inputs | human input |

## Browser and Manual Observations

The gated xUnit hosts run the shipping merchant and admin bundles in Chromium against real loopback APIs, both PostgreSQL databases, signed transports and the local simulator; a standalone Playwright run lacks their seeded orders and clocks. Each suite keeps its own identities. Inspect both the outer xUnit result and the nested Playwright result.

- **P0614-DOC-TRANSFER** — `P0613BrowserTests.FinanceCorrectsInvoiceAndMerchantDownloadsBothVersions`: finance previews v2, recovers the committed POST after a deliberately lost browser response; the harness applies the transfer at the permit boundary; the source payer downloads v1/v2 and the separate payment confirmation; the destination browser is denied. Artifacts under `$P0614_EVIDENCE/p0613/` (`browser-observations.json`, `browser-postgres.json`, `playwright.log`, `merchant-invoice-v1.pdf`, `merchant-invoice-v2.pdf`, `merchant-confirmation.txt`, screenshots); compare `shasum -a 256` of both PDFs with the database rows.
- **P0614-CAPACITY** — `P0603BrowserTests` theory cases `applied` and `drift-resolved` (`p0603/<case>/<run>/capacity-reduction.png`, `build-and-postgres.json`): funded order, retained/omitted shop IDs, conflict and owner resolution, one successor, preserved funds. Composed fact 1 supplies the exact roster; `P0604` the final-slot race.
- **P0614-PERMIT** — `P0606BrowserTests` (`03-admin-preview.png`, `06-admin-cutover-acked.png`, `build-and-postgres.json`) separates the courtesy grant from funded intervals; `P0607BrowserTests` (`01-shop-owner.png`, `03-applied.png`, `checkpoints.json`, `document-access.json`) shows the three-owner workflow, lost-response recovery, permit cutoff and one binding. `P0605` is the normal-at-renewal wait. Known permit deadlines are observed; unknown disconnected queues are acknowledged, never inferred.
- **P0614-REFUND** — `P0610BrowserTests.FinanceAndMerchantRecoverOriginalRefund` (`p0610/finance-unknown-1440.png`, `finance-confirmed-390.png`, `refund-demo.json`): 6,000 requested vs 4,500 approved with explicit `EndAt`, delayed confirmation, one original send/read for that request; the fixture's separate failed 2,000-paise case is counted separately. Composed fact 2 supplies the cumulative cap.
- **P0614-ACCEPTANCE** — the PR handoff records, per story, the command, run-scoped identifiers, clock instants, visible statuses/denials, refund paise, access outcome, SQL/HTTP evidence and artifact path, plus SHAs, UTC run times, tool versions, browser totals including skips and build identities.

## Known Prerequisite Gap

- **G1 — funded reduction held for review on a term with a spare-slot shop** (owning range #911 P06-03 merchant reduction fence × #912 P06-04 slot assignment). `MerchantReductionReservationStore.SourceMatches` (`src/backend/Intelibill.Infrastructure/Licensing/`) requires every source shop's `shop_license_states.starts_at` to equal the term start, but a slot-assigned shop starts at its own coverage instant (`2028-02-14T04:30Z`). The reserve receipt therefore answers `NeedsReview / SourceChanged` and the licensing schedule becomes `NeedsReview`. The outcome is safe — no successor, no demotion or expiry, payment evidence preserved, one finance review case — but story 2's owner-selected renewal does not apply on such terms. Composed fact 1 pins the current recoverable outcome and asserts the slot shop's `starts_at`; when the owning slice fixes the fence, that assertion block becomes the applied-successor assertion (retained = East + North, Central expired, one paid successor). Acceptance row "2" stays open for slot-assigned terms.

## Human Inputs (not enabled, not inferred)

- Published refund eligibility and approval policy, and the transfer consent / remaining-coverage policy wording.
- Provider legal billing identity and the externally issued original and corrective accounting PDFs (fixtures here are synthetic).
- The invoice-issuance target; the aggregate overdue missing-invoice attention queue belongs to the reporting/attention work, not this record.
- Live Razorpay account, keys, webhook secrets and supported checkout methods. No live money movement is authorized.

## Verification Command Matrix

Each line is a distinct check against the committed head; measured counts, exits and artifact paths are in the PR handoff.

```bash
dotnet build src/licensing/Intelibill.Licensing.slnx -c Release
dotnet build src/backend/Intelibill.slnx -c Release
(cd src/frontend && bun run build)
(cd src/admin-frontend && bun run build)
INTEGRATION_TEST_PARALLELISM=12 scripts/dev/test-integration.sh --licensing --filter 'FullyQualifiedName~P0614Tests' --logger 'console;verbosity=normal'
INTEGRATION_TEST_PARALLELISM=12 scripts/dev/test-integration.sh --licensing --filter 'FullyQualifiedName~P0601Tests.ProrationV1VectorsMatchPolicy|FullyQualifiedName~P0601Tests.OwnerQuotesLeapMonthIncreaseFromPurchasedGross|FullyQualifiedName~P0601Tests.EligibilityOffersOnlyTheApplicableNextSteps|FullyQualifiedName~P0601Tests.TrialUpgradeConvertsThroughAFullPaidPurchase|FullyQualifiedName~P0602Tests.FullPaymentQueuesOneIncreaseWithoutCreatingATerm|FullyQualifiedName~P0602Tests.ReservationDecisionSurvivesReplayAndKeepsOriginalAnchor|FullyQualifiedName~P0603Tests.AdmissionAndReductionConsumeSerializeAtTheFenceAndRecoverWithOriginalFunds|FullyQualifiedName~P0604Tests.FinalSlotReceiptsCompeteForOneRemainingTermAndGrantNoShopMembership|FullyQualifiedName~P0605Tests.PermitReportedAfterSchedulingDefersSuccessorUntilExactExpiry|FullyQualifiedName~P0606Tests.CourtesyBridgeConservesThePurchasedSlotAtEveryBoundary|FullyQualifiedName~P0607aTests.LiveReviewCheckRejectsLostAndRegainedAuthority|FullyQualifiedName~P0607Tests.AgreedIntervalsConserveOneFundedSlot|FullyQualifiedName~P0607Tests.ApplyingTransferSerializesWithCompetingMutation' --logger 'console;verbosity=normal'
INTEGRATION_TEST_PARALLELISM=12 scripts/dev/test-integration.sh --licensing --filter 'FullyQualifiedName~P0608Tests.MemberRequestsRefundWithoutChangingTermAccessBalanceOrShopStatus|FullyQualifiedName~P0608Tests.RefundRequestContractPinsHashStatesBoundsAndMigrationAction|FullyQualifiedName~P0609Tests.MfaOlderThanFiveMinutesCannotPreviewApproveOrRecover|FullyQualifiedName~P0609Tests.ConfirmedPlusHeldCountsEachReservationOnce|FullyQualifiedName~P0610Tests.InterveningPaidCapacityIncreaseMovesDelayedRefundAccessToNeedsReview|FullyQualifiedName~P0610Tests.LostSendResponseAndLeaseExpiryNeverCauseAnotherPost|FullyQualifiedName~P0611Tests.InvalidOversizedAndNonPdfContentIsRejected|FullyQualifiedName~P0611Tests.ExactlyTenMiBAndTheNextIstDayAreAcceptedAtTheirBoundaries|FullyQualifiedName~P0612Tests.ContractFixtureMatchesSharedConstantsAndNoInlinePdfSurfaceExists|FullyQualifiedName~P0612Tests.MemberListsOnlyTruthfulInvoiceMetadataForTheirOrder|FullyQualifiedName~P0613Tests.ConcurrentCorrectionsConvergeOnOneSuccessor' --logger 'console;verbosity=normal'
INTEGRATION_TEST_PARALLELISM=12 scripts/dev/test-integration.sh --filter 'FullyQualifiedName~P0411LicensingTests.AcceptanceAndReinstatementRaceForTheFinalSeat|FullyQualifiedName~P0502LicensingTests.Lease_and_snapshot_evidence_agree_for_each_reachable_deadline|FullyQualifiedName~LicenceActionClassificationTests.Inventory_pins_routes_handlers_writers_and_reconciliation_actions' --logger 'console;verbosity=normal'
dotnet test tests/backend/unit/Intelibill.Api.Unit.Tests --filter 'FullyQualifiedName~MerchantCapacityQuoteRelayTests|FullyQualifiedName~MerchantRefundRequestRelayTests'
P0603_RUN_BROWSER=1 P0603_EVIDENCE_DIR="$P0614_EVIDENCE/p0603" INTEGRATION_TEST_PARALLELISM=12 scripts/dev/test-integration.sh --licensing --filter 'FullyQualifiedName~P0603BrowserTests' --logger 'console;verbosity=normal'
P0606_RUN_BROWSER=1 P0606_EVIDENCE_DIR="$P0614_EVIDENCE/p0606" INTEGRATION_TEST_PARALLELISM=12 scripts/dev/test-integration.sh --licensing --filter 'FullyQualifiedName~P0606BrowserTests' --logger 'console;verbosity=normal'
P0607_RUN_BROWSER=1 P0607_EVIDENCE_DIR="$P0614_EVIDENCE/p0607" INTEGRATION_TEST_PARALLELISM=12 scripts/dev/test-integration.sh --licensing --filter 'FullyQualifiedName~P0607BrowserTests' --logger 'console;verbosity=normal'
P0610_RUN_BROWSER=1 P0610_EVIDENCE_DIR="$P0614_EVIDENCE/p0610" INTEGRATION_TEST_PARALLELISM=12 scripts/dev/test-integration.sh --licensing --filter 'FullyQualifiedName~P0610BrowserTests' --logger 'console;verbosity=normal'
P0613_RUN_BROWSER=1 P0613_EVIDENCE_DIR="$P0614_EVIDENCE/p0613" INTEGRATION_TEST_PARALLELISM=12 scripts/dev/test-integration.sh --licensing --filter 'FullyQualifiedName~P0613BrowserTests' --logger 'console;verbosity=normal'
```

A zero-match filter or a skipped gated browser fact leaves its row open. Expected discovery: 3 `P0614Tests` facts, 13 and 11 named licensing facts, 3 merchant facts, the two unit relay classes, two `P0603BrowserTests` theory cases and one fact each for `P0606`, `P0607`, `P0610` and `P0613` browser suites.
