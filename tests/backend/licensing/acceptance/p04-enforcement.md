# P04 online enforcement acceptance

[P04 §8](https://github.com/chanakya-net/intelibill/issues/826) defines the action matrix. [The entry-point inventory](p04-enforcement-inventory.tsv) pins 231 merchant route entries, 139 Wolverine handler calls, 12 writer types, and all six reconciliation actions. The writer entries include the scheduled writers, offline poster, and bulk row executors. `LicenceActionClassificationTests` compares live discovery against that inventory. Mixed entry points still decide from the actual source action, including a member removal, quarantine disposal, or existing committed outcome.

## Matrix and regression evidence

The PostgreSQL tests use real source transactions and local schema-owner enrollment. Fixture grants and signed delivery use test keys. Merchant and admin release suites use routed fixtures. The dedicated Chromium journey uses the shipping merchant build and real loopback merchant and licensing APIs. No live provider was contacted.

| Row | Pending activation | Trial, active or grace | Expired, trading suspended, or closed | Security suspended | Positive and bypass or race evidence |
| --- | --- | --- | --- | --- | --- |
| M01 Identity, sessions and support | Live identity permissions | Live identity permissions | Live identity permissions | Login and account security remain available. Scoped shop data is restricted. | `P0418Tests.History_and_identity_obey_the_suspension_scope`, `P0408dLicensingTests.DeviceRevokedDuringAProtectedReadReceivesNoShopData` |
| M02 Basic setup | Owner preparation only | Existing setup rules | Identity, contact and recovery necessities | Block affected shop data | `P0408dLicensingTests.PendingOwnerPreparesTaxWithoutTradingOrCoverage`, `RecoveryAllowsIdentityAndContactOnly`, `FenceContentionAndRevocationCannotSaveSettings` |
| M03 New trading | Refuse new work | Existing sale, intake, receiving, period and expense rules | Refuse even a pre-existing draft | Refuse | `P0405LicensingTests` goods, service, tax and credit cases, `P0406LicensingTests.Partial_intake_crosses_deadline_without_posting_row_B`, `P0407LicensingTests.PreExpiry_order_cannot_receive_at_deadline`, `P0409LicensingTests.Licence_is_reevaluated_after_waiting_for_the_posting_period`, `P0418Tests.Provider_suspension_and_sale_serialize_on_the_merchant_fence` |
| M04 Operational capacity | Refuse expansion | Existing roles and allowances | Refuse new capacity | Refuse | `P0408a/b/c/dLicensingTests`, `P0410LicensingTests.Open_waits_for_the_shop_licence_fence_before_any_business_write`, `P0411LicensingTests.Final_seat_serializes_with_a_lower_allowance_revision`, `P0418LicensingTests.Sources_operational_capacity_writes_refuse_after_expiry` |
| M05 History, print, reports and exports | Existing records | Existing permissions | Preserve authorized reads and print | Block data exposure | `P0415LicensingTests` history, export and response protection, `P0418Tests.History_and_identity_obey_the_suspension_scope`, Chromium history and invoice print |
| M06 Till completion | Proven open till | Existing cashier and manager rules | Counted closure and authorized review | Refuse | `P0410LicensingTests.Expiry_denies_old_open_draft_but_allows_counted_close`, `Expired_staff_count_requires_manager_review_and_keeps_date_rules`, `P0403Tests.TillReplayRequiresRecoveryButNotTradingPermission` |
| M07 Existing dues | Current proven obligation | Existing tender and authority rules | Settle up to eligible outstanding | Refuse | `P0412LicensingTests` concurrent settlements, stale authority, foreign sources and advance refusals |
| M08 Existing payment allocation and reversal | Proven receipt | Existing authority and posting rules | Preserve eligible allocation and reversal | Refuse | `P0412LicensingTests.Existing_receipt_operations_replay_while_restricted`, `P0415LicensingTests.Keyed_payment_delivery_contention_is_uncertain_and_receipt_recovers_one_posting` |
| M09 Source corrections and returns | Proven eligible source | Existing source constraints | Eligible source completion | Refuse | `P0413a/b/c/dLicensingTests` frozen sources, posting and replay, `P0409LicensingTests.Restricted_shop_keeps_an_eligible_correction_but_refuses_one_that_adds_an_expense`, `P0418LicensingTests.Sources_corrections_decide_inside_the_licence_fence` and source-authority regressions |
| M10 General stock | Refuse new stock work | Existing stock rules | Refuse unrelated stock gain | Refuse | `P0406LicensingTests.Keyed_count_gain_rechecks_access_after_receipt_completion`, `P0413dLicensingTests.Restricted_general_stock_cannot_be_posted_as_a_receipt_correction`, `P0418LicensingTests.Legacy_batch_route_keeps_the_quantity_protocol_denial_at_expiry` |
| M11 Safety disposition | Existing safety obligation | Existing stock and disposition rules | Allow eligible quarantine disposal. Release to trading requires operating access. | Refuse | `P0414LicensingTests.Eligible_release_stops_at_paid_deadline_and_owner_can_dispose`, `Restricted_disposal_requires_eligible_source_and_conserves_stock`, `Scheduled_security_suspension_blocks_disposal_after_stock_lock_wait` |
| M12 Existing offline recovery and outcome lookup | Preserve evidence under current authority | Existing lease, retry and reconciliation rules | Preserve already-issued recovery and committed outcomes | Ordinary recovery refused | `P0418LicensingTests.Previously_issued_offline_invoice_replays_after_expiry`, `P0405LicensingTests.Committed_sale_replays_only_under_current_recovery_authority`, `P0418Tests.Committed_invoice_and_replay_authority_survive_restriction_transitions`, `P0415LicensingTests` |
| M13 Billing and member removal | Own billing and live removal authority | Existing roles and capabilities | Payment and administration continue within scope | Reviewed own billing and authorized member removal | `P0403Tests.PaymentCannotLiftARecordedAccountSuspension`, `P0404Tests.RefundedObservationAndReviewDoNotReleaseAccountHold`, `P0415LicensingTests.Account_security_keeps_source_authorized_member_removal_available`, `P0416Tests` closure and `P0417Tests` reviewed reopening |
| M14 Security restriction | Block affected shop data and trading | Same | Same | Same | `P0418Tests.Request_gated_routes_refuse_before_binding_and_never_return_shop_data`, `P0415LicensingTests.Applied_security_stops_realtime_for_the_affected_shop`, `P0417Tests.SecuritySuspensionSurvivesReopening` |

The merchant billing inventory includes orders, versioned payment confirmation documents, policy and status, cancellations, invitations and closure. Provider refund review evidence concerns the admin workflow. The inventoried merchant routes contain no refund-request endpoint.

## Source decisions from the fixed-clock retry

- The PostgreSQL notification witness reads from a separate connection during discount create, replace and disable notifications. It sees committed rows and replacement links. A create denied at the exact trading deadline and a write rolled back before commit send no notification.
- A delayed trading suspension with a known effective instant rejects batch void, purchase order cancellation, supplier deletion and discount disable when their original source is at or after that instant. The tests compare stock, movement, receipt, order, supplier and discount rows before and after the denial. The earlier expiry and lock tests exercise eligible sources.
- Creating or replacing a discount schedules new trading terms. Reactivating a dormant item restores operational capacity. Generating a barcode advances a shop sequence for a new definition. Both batch repricing routes can set new retail terms for future trading. The operating setup succeeds and the same action at the deadline refuses without an additional rule, activation, sequence advance or price revision. The legacy batch route preserves its quantity, cost and supplier protocol checks.
- An existing unused ordinary supplier can be removed before and at expiry. Removal requires an eligible in-shop supplier and owner authority. System suppliers and missing targets retain their original denials. Disabling an existing discount only stops its use. Both actions use the source-correction fence.
- Receipt void, adjustment void, credit-note void, posting-period transitions, unused purchase-order draft deletion, cancel, close, and receipt supplier reassignment complete existing work under the established period, source and authority constraints. Existing service updates and customer edits retain their approved expiry behavior and serialize with suspension. A stock adjustment void restores only the original quantity from the original eligible source. While an exclusive projection fence was held, the historical probes observed committed movement or period rows behind a waiting response. The retry reads PostgreSQL facts while the fence is still held and again after release. Fenced customer creation remains the positive control.
- At 48 hours minus one microsecond from the upstream verification stamp the enrolled sale still posts. At exactly 48 hours, redelivery of the signed snapshot leaves the new sale refused with `503 Licence.VerificationUnavailable`. The warm enrolled sale, committed replay, and sale history read produced zero outbound merchant HTTP calls and zero inbound licensing requests. The existing P04-02 restart regression covers persistence of the proof timestamps.
- The shipping merchant browser preserves an existing cart across a real restriction and release in one signed-in Chromium context. The owner recorded invoice `A/2627/000001`, received a 403 on the next checkout, opened the sales list and original invoice print, then chose **Check again** and recorded `A/2627/000002`. PostgreSQL held two sales and an unchanged original invoice snapshot. The provider restriction passed through Pending and Acknowledged.
- One five-sample pre-commit run on loopback Kestrel and PostgreSQL with fixture keys measured enforcement at 1,836–2,002 ms and acknowledgement at 2,229–2,413 ms, median 2,345 ms. The delivery workers ticked every 2,000 ms and the test polled every 50 ms. Each sample observed Pending before acknowledgement. The target is 60 seconds under healthy connectivity.

## P05 and P08 enrollment prerequisites

The preserved P04 base `c454b4f09ee716e097d82a498d6330751a4dd5b4` posted a legacy lease invoice sold at the trading deadline and synced five minutes later. PostgreSQL recorded one sale and zero held envelopes. This is a P05 rollout blocker assigned to historical permit validation and `LicenseHistoryReview` in [#903](https://github.com/chanakya-net/intelibill/issues/903) and [#904](https://github.com/chanakya-net/intelibill/issues/904). It supplies no passing P04 coverage. The P04 recovery regression submits a pre-deadline invoice from an already issued lease, replays the frozen payload, confirms its original hash, and verifies that security suspension refuses ordinary recovery. P05 permit/review compatibility and P08 migration and enrollment approvals remain required.

## Verification commands

Use the repository root unless a different working directory is listed. Add `/usr/local/share/dotnet` to the front of `PATH` on the verification Mac and set `P0418_EVIDENCE_DIR` to a fresh absolute directory. The run records command argv, working directory, environment, UTC times, Git HEAD, status and test output. Use `P0418_RUN_BROWSER=1` for C08 and the P04 browser on combined verification.

| Check | Working directory | Command |
| --- | --- | --- |
| C01 | root | `dotnet build src/backend/intelibill/Intelibill.slnx` |
| C02 | root | `dotnet build src/backend/licensing/Intelibill.Licensing.slnx` |
| C03 | root | `scripts/dev/test-integration.sh --licensing --filter 'FullyQualifiedName~P0418Tests\|FullyQualifiedName~P0418TimingTests' --logger 'console;verbosity=normal'` |
| C04 | root | `scripts/dev/test-integration.sh --licensing --filter 'FullyQualifiedName~P0403Tests\|FullyQualifiedName~P0404Tests\|FullyQualifiedName~P0416Tests\|FullyQualifiedName~P0417Tests' --logger 'console;verbosity=normal'` |
| C05 | root | `scripts/dev/test-integration.sh --filter 'FullyQualifiedName~P04\|FullyQualifiedName~LicenceActionClassificationTests' --logger 'console;verbosity=normal'` |
| C06 | root | `dotnet test tests/backend/intelibill/unit/Intelibill.Application.Unit.Tests --filter FullyQualifiedName~MemberSeatPolicyTests` |
| C07 | src/frontend/intelibill | `bun run build` |
| C08 | root | `dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --no-build --filter FullyQualifiedName~P0418BrowserTests --logger 'console;verbosity=normal'` |
| G01 | src/frontend/intelibill | `bun run test --watch=false` |
| G02 | src/frontend/intelibill | `bun run build` and `bun run audit:ui:release` |
| G03 | src/frontend/licensing | `bun run test --watch=false` |
| G04 | src/frontend/licensing | `bun run audit:release` |
| G05 | root | `dotnet test tests/backend/shared/P0102ConsumerRegressionTests` and `dotnet test tests/backend/shared/P0103ConsumerRegressionTests` |
| G06 | root | `bash .github/scripts/tests/licensing-stack-contract.test.sh` |
| G07a | root | `P0418_RUN_BROWSER=1 scripts/dev/test-integration.sh --licensing --logger 'console;verbosity=minimal'` |
| G07b | root | `INTEGRATION_TEST_PREGENERATED_HANDLERS=1 INTEGRATION_TEST_PARALLELISM=4 dotnet test tests/backend/intelibill/integration/Intelibill.Integration.Tests --no-build --logger 'console;verbosity=minimal'` |

Independent review and combined-branch verification are required before issue completion. The P04 gate authorizes local and test enrollment only.

## Historical user-fix candidate evidence

The candidate based on `007c44b0` passed the backend, licensing and merchant builds. The Mac used .NET 10.0.300 as resolved by `global.json`, Bun 1.3.11, Docker PostgreSQL 17 and the repository's Playwright Chromium. The full merchant release audit passed 197 cases. Merchant and admin unit suites passed 3,386 and 138, and the admin release audit passed 15.

The full licensing integration suite passed 724, with 35 opt-in browser cases skipped in that invocation. The P04 Chromium case ran separately with `P0418_RUN_BROWSER=1` and passed against the same built candidate. The exact isolated `P0307Tests.UpgradeRetainsEvidenceAndReviewCannotBypassGuards` passed, including restoration of the successor migrations before the final billing check.

The focused two-API P04 cases passed 13, prerequisite licensing cases passed 105, and the merchant P04 and classification gate passed 481. The focused PostgreSQL source probes passed 36, member-seat policy passed 8, and the full merchant PostgreSQL suite passed 1,673. Backend unit suites passed 2,908. Both shared consumer checks passed (15 and 12), and the stack contract passed for 11 services.

The activation-column source probe found only its migration declaration. The coordinator's evidence includes the full gate logs and command metadata, fixed-clock and PostgreSQL probes, live acknowledgement samples, and Chromium screenshots and observations.

## Previous attempt evidence

The focused source run passed 36 PostgreSQL cases. The broader merchant P04 and
classification run passed 481. The two-API P04 fixtures passed 13, the
prerequisite licensing run passed 105, and the pinned real-API Chromium journey
passed. The full merchant PostgreSQL suite passed 1,673. Backend unit suites passed 2,908. Merchant and admin unit suites passed
3,385 and 138, the admin release audit passed 15, and the shared consumer checks
passed 15 and 12.

The complete merchant release audit with Bun 1.3.11 reported 180 passed and 17
failed. The merchant and admin web sources, shared frontend packages and Bun
lockfile match the immutable issue base. The pinned failures include three
bounded-authority request-count assertions, two definition-creation fixtures,
one expense-correction fixture, nine shop-setup fixtures, and two
supplier-return fixtures. The pending purchase-order fixture waits on a New
button that stays disabled, consistent with the M03 P04-07 gate on new orders.
The first audit with Bun 1.3.14 also failed the purchase receiving date
assertion, which passed in the pinned rerun.

The full licensing suite reported 724 passed, one failed, and 34 optional
browser cases skipped. The required P04 Chromium journey passed.

The older P0307 migration check
`UpgradeRetainsEvidenceAndReviewCannotBypassGuards` returned
`503 Billing.DelegationUnavailable` in the full licensing run, in isolation,
and from a fresh archive of the immutable issue base. PostgreSQL reported
`42P01` when the current billing delegation query read closure and reopening
relations after the attempted downgrade. The migration sequence had already
removed later schema before the older downgrade guard refused the operation.

These failures stopped the previous attempt. Its handoff records commands, environments, failing cases and evidence paths.
