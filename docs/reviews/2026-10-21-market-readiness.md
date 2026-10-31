**Intelibill: web and API market-readiness assessment**

Reviewed 21 October 2026 against commit `5485507ef743818b36d751262ca85591f0c0c10f` on `main`. Target confirmed by the owner: **public SaaS for Indian retail shops**. Android/mobile is excluded; responsive browser behavior remains in scope. This is an assessment, not an implementation change.

**Decision: do not open unrestricted public onboarding yet.** There is a substantial retail product here, with much more functionality than the May feature snapshot suggests. The remaining work is concentrated around identity boundaries, reliable financial posting, tax documents, operational recovery, and the SaaS customer lifecycle. Those weaknesses affect trust in money, stock and customer data. Adding more dashboards or AI features would not address them.

A rewrite is not indicated. Preserve the existing domain workflows, server-side pricing, sale idempotency, purchasing locks, credit-note redemption protection, offline queue model and printing work. Correct the broken boundaries and establish release gates around them.

**1. What this review establishes**

The review traced Angular routes, forms, facades, persistent browser stores and production service-worker behavior; API controllers, request validators, handlers, entities, repositories, migrations and auth/session paths; CI and infrastructure definitions; and relevant positive/negative tests. Three focused code reviews cover business workflows, security and web behavior. High-impact paths were also independently inspected during consolidation.

There are three evidence levels:

- **Executed:** local build/test outcomes, PostgreSQL catalog inspection, and a controlled browser reproduction using the actual production service worker.
- **Code-supported:** explicit implementation paths, concrete failure scenarios and missing invariants. These are not claims that every scenario was reproduced against a deployed service. In particular, concurrent refund/refresh cases require deterministic regression tests.
- **Not found / not verified:** capabilities missing from the inspected repository, or operational assurances that cannot be established from source. A separately hosted billing/help site or manually configured cloud alert may exist; none was verified here.

No production API, cloud account configuration or real customer records were tested or changed. No SMTP/provider end-to-end delivery, load benchmark, production penetration test, actual payment settlement, physical printer/scanner certification or restore drill was performed. Source-level tax findings are compared with official requirements below; this does not certify the complete product's tax treatment.

Full supporting reviews, including source locations and acceptance criteria:

- [Backend business workflows](/Users/chanakya/projects/intelibill/docs/reviews/2026-10-21-market-readiness-evidence/backend-business.md)
- [API authentication, security and tenancy](/Users/chanakya/projects/intelibill/docs/reviews/2026-10-21-market-readiness-evidence/api-security.md)
- [Web features and customer workflows](/Users/chanakya/projects/intelibill/docs/reviews/2026-10-21-market-readiness-evidence/web-workflows.md)
- [Executed verification record](/Users/chanakya/projects/intelibill/docs/reviews/2026-10-21-market-readiness-evidence/verification.md)

Validation completed: 1,551 backend unit tests and 344 PostgreSQL integration tests passed under a diagnostic vulnerability-warning override; the normal backend build fails. The frontend production build passed with budget warnings, and its unit suite passed 1,547 tests with four skipped. Selected web/print browser checks passed 38 tests across two runs, skipped five and failed one login screenshot baseline comparison. The production service-worker isolation probe reproduced a cross-identity response. The verification record explains environment differences, test scope and these qualifications; passing tests do not invalidate the uncovered gaps.

**2. Features implemented well—and the boundary of that assessment**

“Implemented well” means a useful, substantive foundation or a particular sound mechanism. It does not mean the entire module is launch-approved despite the defects in the next section.

| Feature | What is already good | What prevents calling it finished |
|---|---|---|
| Product catalog | Search, pagination, active status, barcodes, generated labels and HSN lookup are implemented. | Existing-shop migration/import and safe browser shop transitions are missing. |
| Stock intake | Item, physical batch, stock movement and supplier liability are coordinated; bulk UI keeps unsuccessful rows for correction. | Late row failures can still leave tracked records that another successful row commits. |
| Stock adjustments | Reason/direction, before/after quantity and cost snapshots, reversal and role checks exist. Inventory aggregate uses PostgreSQL optimistic concurrency. | Separate batch-correction path breaks movement and supplier-ledger invariants. |
| POS | Mixed goods/services, barcode/name lookup, batch selection, customer due, discount preview and server calculation exist. | Unknown-result retry can duplicate a sale; shop switching can mix carts; fractional retail quantities are truncated. |
| Sale idempotency in the API | Required key, request hash, replay handling and database uniqueness are meaningful safeguards. | Browser generates a new key on each retry; other money-changing commands do not implement equivalent protection. |
| Discount engine | Shared preview/record calculation, configured rule selection and cent allocation. | Inclusive/exclusive tax bases differ in the below-cost guard; discount policy needs coherent economic tests. |
| Sales returns | Preview, remaining quantities, restock/wastage, original prices, due reduction and credit-note payout exist. | No shared concurrency guard for service/wastage refunds; partial-refund exports can be unbalanced. |
| Store credit | Issuance, expiry, verification, redemption, partial balance and void behavior exist. Redemption uses a row lock/transaction. | Store credit is not automatically a statutory GST credit note; settlement split must survive accounting export. |
| Purchasing | Draft/place/cancel/close, partial receiving, history and printing. Receipt processing locks the PO and checks remaining quantities. | Return-to-supplier, received-document correction and multi-lot receiving need a complete commercial workflow. |
| Customer and supplier accounts | Khata-style ledgers, collections/payments and inventory/procurement links are substantive. | Collections are not allocated back to invoices; retries can double-post payments; displayed credit facts can be wrong. |
| Expenses | Categories, recording, correction and voided-state display exist. | Status filtering is only over the loaded page, and profit semantics do not include operating expenses. |
| Dashboard and P&L | Sales, returns, stock losses/value, receivables/payables, low-stock/expiry and PO information exist. | Old-shop results remain after switching; UTC reporting boundaries and profit labels mislead. |
| Documents and exports | A4/80mm receipts, purchase-order and credit-note print, XLSX/PDF/Tally export, P&L XLSX. Selected print browser tests pass. | GST document data, offline seller details, return-period attribution and balanced accounting output need correction. |
| Offline billing | Explicit enablement, IndexedDB snapshots, leased numbers, frozen prices, local stock and durable queue/statuses. | Cross-identity API cache, trusted monetary arithmetic, expired-lease recovery and unresolved-review workflow block a broad offline promise. |
| Authentication primitives | bcrypt, hashed short-lived reset tokens, signature/audience/lifetime JWT validation, PKCE and Key Vault signing support. | Enrollment/linking, global identity administration and session revocation contain major lifecycle flaws. |
| Authorization | Many handlers reload membership/role and verify the target record's shop, independently of UI guards. | This does not replace missing RLS or fix shared-account ownership boundaries. |
| Localization and web layout | Nine locale bundles contain 1,703 matching keys; responsive POS and detailed print layouts are present. | Translation quality and assistive-technology usability are not proven by matching keys. Core cashier controls lack labels. |
| Delivery infrastructure | Build/tests, image scanning, digest-based promotion, separate migration job, readiness probes and image rollback exist. | Current normal build fails; production business smoke, recovery proof, capacity validation and alert ownership are insufficiently evidenced. |

Representative strong implementation: [sale replay](/Users/chanakya/projects/intelibill/src/backend/Intelibill.Application/Features/Sales/Commands/RecordSale/RecordSaleCommandHandler.cs:49), [inventory concurrency](/Users/chanakya/projects/intelibill/src/backend/Intelibill.Infrastructure/Data/ApplicationDbContext.cs:61), and [release promotion](/Users/chanakya/projects/intelibill/.github/workflows/deploy.yml:746). Supporting reviews contain the other source references and test coverage.

**3. Existing features requiring correction before launch**

Priority terminology: **Blocker** means do not expose the affected workflow to public customers; **high** means resolve before real trading or explicitly exclude that capability; **medium** means usability/operational work with a defined release decision. Security severity and launch priority are separate concepts.

**R01 — Blocker: one identity can receive another identity's cached API data. Executed reproduction.**

The production worker caches `/api/**` for one hour and falls back after eight seconds. The cache is not partitioned by user/shop. A local harness served the actual built `ngsw-worker.js` and `ngsw.json`: a request authorized as Shop A populated a response; the same URL authorized as Shop B, with a slow network, received Shop A's data after 8,005 ms. No real tenant records were involved. Backend authorization cannot protect a response served entirely from browser cache.

Remove broad protected-API caching and use the explicit scoped offline stores. Test A→B, logout→different user, slow responses, offline mode and worker upgrades. [Configuration](/Users/chanakya/projects/intelibill/src/frontend/ngsw-config.json:55), [result](/Users/chanakya/projects/intelibill/docs/reviews/2026-10-21-market-readiness-evidence/service-worker-cache-result.json), [reproduction harness](/Users/chanakya/projects/intelibill/docs/reviews/2026-10-21-market-readiness-evidence/service-worker-cache-probe.mjs).

**R02 — Blocker: unverified signup and automatic OAuth linking permit pre-account takeover. Code-supported.**

An attacker registers a victim's email with a password the attacker knows. When the genuine owner later uses Google, the provider account is automatically linked by email to that existing user. The original password and sessions survive. If the genuine user builds their shop, the attacker can log in to that same account. This chain does not require a forged Google token.

Implement verified enrollment and explicit account linking/claiming, including revocation of pre-claim credentials. Apply the same ownership proof to identifier changes. [Registration](/Users/chanakya/projects/intelibill/src/backend/Intelibill.Application/Features/Auth/Commands/RegisterWithEmail/RegisterWithEmailCommandHandler.cs:22), [automatic linking](/Users/chanakya/projects/intelibill/src/backend/Intelibill.Application/Features/Auth/Commands/ExternalLogin/ExternalLoginCommandHandler.cs:37).

**R03 — Blocker: shop administration can take control of a global identity. Code-supported.**

An owner may change a staff member's global recovery email and globally disable that user. The member may separately own another shop. Ownership of Shop A should not authorize changing the recovery identity that owns Shop B. With working recovery email, changing it to an owner-controlled address allows a password-reset takeover.

Separate account ownership from membership administration. Owners should invite, assign roles and suspend membership within their shops; account holders control verified sign-in/recovery identifiers. [Global mutation](/Users/chanakya/projects/intelibill/src/backend/Intelibill.Application/Features/Users/Commands/EditShopUser/EditShopUserCommandHandler.cs:23).

**R04 — Blocker: disable/reset does not reliably terminate access. Code-supported.**

Password login checks `IsLoginEnabled`; refresh and external login do not. More seriously, an old still-valid access token can update its profile and obtain a new refresh token after a password reset revoked the previous refresh tokens. This extends compromise beyond the normal access-token expiry window.

Centralize session validity/version checks across all token-minting routes, including profile/shop operations. Test disable, password reset/change, refresh races and existing hub connections. [Refresh](/Users/chanakya/projects/intelibill/src/backend/Intelibill.Application/Features/Auth/Commands/RefreshToken/RefreshTokenCommandHandler.cs:19), [profile token minting](/Users/chanakya/projects/intelibill/src/backend/Intelibill.Application/Features/Users/Commands/UpdateMyProfile/UpdateMyProfileCommandHandler.cs:40).

**R05 — High: core database tenancy protection is missing. Executed catalog inspection.**

A freshly migrated PostgreSQL database reports RLS disabled on core tables including `sales`, `inventory`, `inventory_batches`, `customers`, `shop_memberships`, `supplier_ledger_entries` and returns. Some later tables have it. This contradicts the blanket RLS description. The integration role reports `rolsuper=true` and `rolbypassrls=true`; the test factory also omits the separately registered production session interceptor when replacing context options.

Existing application membership and shop predicates provide real protection; this finding is not a demonstrated arbitrary cross-shop API read. Add complete policies and test the actual restricted runtime role, absent context, alternating pooled connections and cross-tenant writes. [Catalog evidence](/Users/chanakya/projects/intelibill/docs/reviews/2026-10-21-market-readiness-evidence/fresh-schema-rls.txt), [test factory](/Users/chanakya/projects/intelibill/tests/backend/integration/Intelibill.Integration.Tests/ApiWebApplicationFactory.cs:87).

**R06 — High: retries can duplicate sales and money entries. Code-supported.**

The browser creates a fresh sale idempotency key for every submission. A sale commits, its response is lost, and the cashier retries: the server sees a different operation. Customer/supplier payment requests have no operation identity at all, so the same failure pattern doubles the ledger entry; supplier payment also creates a second expense.

Persist one checkout intent/key/payload until delivery is reconciled. Extend scoped idempotency to payments, returns and other non-repeatable writes. Acceptance must simulate response loss after commit and replay after browser restart, not only disable double-clicks. [Browser key](/Users/chanakya/projects/intelibill/src/frontend/src/app/features/sales/pages/new-sale-page.cart-submit.service.ts:236), business review F8.

**R07 — High: switching shops does not switch all state. Code-supported.**

A nonempty root-scoped cart is not replaced when another shop's draft loads; persistence then saves the retained cart under the new shop ID. Dashboard and several directories also load on construction/filter change rather than shop transition, so the title/token can say B while data still belongs to A. Old asynchronous responses can further complicate the transition.

Implement a single scoped-state transition: detach old state, save it under its original identity/shop, cancel or reject old requests, reset selections, reload and revalidate route permission. Test independent nonempty carts in A/B and switching during requests. [Cart loading/persistence](/Users/chanakya/projects/intelibill/src/frontend/src/app/features/sales/services/sale-cart-state.service.ts:33), web review WEB-02/04.

**R08 — High: stock correction can corrupt audit and payable balances. Code-supported.**

For a decrease, the correction handler creates an OUT transaction with a positive absolute quantity. The domain rejects it; the handler ignores the error and saves the stock change. Separately, after partial sales it reverses the original purchase liability and recreates it from remaining stock. Buy 10 at ₹100, sell four, then correct remaining cost to ₹110: the handler changes payable from ₹1,000 to ₹660 without a supplier payment/return. A retail price correction also voids the original batch, obstructing restock of earlier sales that reference it.

Separate price updates, stock-count adjustments and purchase-document corrections. Preserve batch lineage and original receipts; use explicit financial deltas and propagate invariant failures. [Correction path](/Users/chanakya/projects/intelibill/src/backend/Intelibill.Application/Features/Inventory/Commands/UpdateInventoryBatch/UpdateInventoryBatchCommandHandler.cs:107), business review F2/F3.

**R09 — High: failed bulk rows can persist partial records. Code-supported.**

Bulk intake shares one change tracker, continues after row errors, and saves if any row succeeds. A zero-cost row passes input validation; its batch and stock transaction are added before the zero-value supplier entry is rejected. Another successful row causes those partial records to commit, although the response calls the row failed and the aggregate stock was not updated.

Use validated side-effect-free preparation or properly isolated row transactions/savepoints and tracker restoration. Verify failure leaves no item, batch, stock or ledger effects. Explicitly decide how free stock is represented. [Bulk save](/Users/chanakya/projects/intelibill/src/backend/Intelibill.Application/Features/Inventory/Commands/AddInventoryBatch/AddInventoryBatchCommandHandler.cs:53), [partial tracking](/Users/chanakya/projects/intelibill/src/backend/Intelibill.Application/Features/Inventory/Services/BatchFactory.cs:53).

**R10 — High: refund concurrency and accounting are incomplete. Code-supported; race not dynamically reproduced.**

Service/wastage returns validate remaining returnable quantity from a read, then insert refund records without locking/updating a shared sale aggregate. Two concurrent requests can each validate the full amount. Existing inventory concurrency does not protect paths that never touch inventory. Partial-refund accounting has a separate deterministic defect: a ₹118 original value (₹100+₹18) with an approved ₹50 refund can produce a Tally credit note with debit ₹118 and credit ₹50. Mixed due reduction and store credit also lose their settlement split in export.

Protect return/void aggregates and add idempotency; persist original goods valuation separately from actual financial/tax adjustment and settlement. Require every voucher to balance for full, partial, zero, mixed-settlement and concurrent returns. [Return snapshot](/Users/chanakya/projects/intelibill/src/backend/Intelibill.Application/Features/Sales/Services/Returns/SaleReturnValidator.cs:43), business review F4/F7.

**R11 — High: offline billing needs validation and an operator recovery path. Code-supported.**

Offline sync checks signs and `paid + due = header total`, but trusts supplied line/header monetary values without reconciling them. A line totaling ₹118 with header/paid ₹1 can be accepted under the traced path. Preserve historical prices, but validate arithmetic using those historical inputs. Recomputing against today's catalog is not the solution.

The UI displays NeedsReview counts but does not provide a queue-detail/resolution journey; the backend has reconciliation entities without a complete read/resolve API. An invoice issued during a valid lease can become permanently NeedsReview if synced after lease expiry. Offline print additionally constructs blank seller address/GST details, and its route lacks the POS's expired-token offline grace.

Build a durable review/recovery workflow with immutable original invoice data, historical-lease validation, arithmetic checks, support export and authorized resolution. Fix offline print eligibility and seller snapshots. [Offline print](/Users/chanakya/projects/intelibill/src/frontend/src/app/features/sales/pages/sale-invoice-print-page/sale-invoice-print-page.component.ts:135), business F5 and offline gaps; web WEB-05/07.

**R12 — High: financial reports disagree across periods and concepts. Code-supported.**

- Sales exports select sales by date, then attach their lifetime returns. A June sale refunded in July puts July's return in a rerun of June, while July omits it. Select transactions by their own posting dates and retain sale references separately.
- Daily/monthly query bounds and Tally dates use UTC. A sale at 02:00 IST on 21 October falls on 20 October in these paths. Store UTC instants but derive accounting dates/bounds in the shop's business timezone.
- “Net profit” excludes operating expenses; `MarginPercent` uses profit/cost, which is markup. Rename or implement the promised metric. Do not simply deduct every expense: supplier payments are also recorded as expenses and would double-count purchase costs already in COGS.
- Tax-inclusive below-cost protection compares gross receipt with net cost. A ₹100 net cost, ₹118 tax-inclusive price and ₹10 pre-tax discount passes the guard while yielding ₹90 taxable revenue and a ₹10 loss.

[Export period selection](/Users/chanakya/projects/intelibill/src/backend/Intelibill.Application/Features/Exports/Sales/Services/SalesExportDatasetBuilder.cs:34), [UTC window](/Users/chanakya/projects/intelibill/src/backend/Intelibill.Infrastructure/Repositories/SaleRepository.cs:85); business F6/F9/F10 and profit gap.

**R13 — High: the GST billing promise is broader than the stored document model. Code and official-source comparison.**

Online invoice numbers are `INV-yyyyMMdd-xxxxxxxx`: 21 characters with a random suffix. GST Rule 46 requires a consecutive series unique within the financial year and no more than 16 characters. The model/templates also lack complete buyer GST identity/place-of-supply and component tax representation; offline print omits even configured seller GST details. Reprints load live seller details instead of a complete issued-document snapshot. [Online numbering](/Users/chanakya/projects/intelibill/src/backend/Intelibill.Application/Features/Sales/Commands/RecordSale/RecordSaleCommandHandler.cs:116), [CBIC Rule 46](https://taxinformation.cbic.gov.in/content-page/explore-rules/1000136/1000001), [GST portal invoice-number guidance](https://tutorial.gst.gov.in/userguide/returns/GSTR_1.htm).

Define supported merchant regimes and transaction types before implementing the final tax contract: registered regular, composition/unregistered, B2B/B2C, intrastate/interstate, goods/services. Then provide fiscal numbering shared safely with offline ranges; correct document types; seller/buyer snapshots; CGST/SGST/IGST as applicable; and coherent return/credit documents and accountant exports. Not every field is required on every small B2C receipt, so this is a scoped implementation gate rather than a blanket judgment about every existing receipt.

E-invoicing/IRN/QR integration is a separate applicability decision. Official GSTN material identifies the turnover-based mandate; either support eligible merchants or exclude them explicitly from first launch. Do not treat it as universally mandatory for all small retailers. [GSTN overview](https://www.gstn.org.in/assets/mainDashboard/Pdf/Final_e-Invoice_Brochure_Design_for_IITF%202025.pdf).

**R14 — High for general retail: customer credit and quantity workflows are incomplete. Code-supported.**

Customer collections reduce account balance without allocating to invoices, so a fully paid account can still show the original invoice as due. The account UI fabricates a credit-limit value from balance/lifetime revenue rather than showing the saved limit, and calls every outstanding sale overdue. Saved credit limits/inactive status are not consistently enforced at credit-sale posting. Implement receipt allocation/current outstanding semantics and an explicit credit override policy.

The web POS also truncates goods quantities to integers and clamps them to at least one. This prevents 0.25 kg or 1.5 m despite decimal quantities in the domain. General grocery/weighted retail needs UOM precision end to end; a packaged-pieces-only launch must state that restriction. [Quantity clamp](/Users/chanakya/projects/intelibill/src/frontend/src/app/features/sales/pages/new-sale-page.cart-selection.service.ts:37), web WEB-08/09; business customer-account gaps.

**R15 — High release gate: the ordinary backend build currently fails. Executed.**

`dotnet build src/backend/Intelibill.slnx` fails with 12 NU1902/NU1903 errors because vulnerability warnings are errors. The resolved runtime dependency includes `System.Security.Cryptography.Pkcs 10.0.0`; `SSH.NET 2024.1.0` appears in the integration-test graph. This is an actual reproducibility failure, not merely an old version observation. The Microsoft advisory lists a fixed .NET 10 package version; verify the complete dependency path and compatibility when updating. The audit does not establish reachability of the vulnerable APIs from Intelibill endpoints. [Microsoft advisory](https://github.com/advisories/GHSA-846f-f764-76mx).

For diagnostic functional tests only, NU1902/NU1903 were downgraded to visible warnings via a command-line MSBuild property. No project safety setting was edited. That run cannot be presented as a passing normal release build.

**4. Capabilities that need implementation for the public SaaS offer**

These are product gaps, separate from fixing existing defects. “Not found” means absent from the reviewed web/API surface, not proof that no external/manual service exists.

| Capability | Minimum useful launch scope | Priority |
|---|---|---|
| Verified enrollment and invitations | Verified email, verified phone if supported, invitation/acceptance, safe linking, recovery and account/member separation. Disable incomplete phone signup until it can support logout/new-device recovery. | Before public signup |
| Session/device administration | Visible active sessions, revoke device/all sessions, reliable disable/reset, security notifications; stronger owner authentication based on risk. | Before public signup |
| Merchant setup | Guided shop/tax setup, invoice series, supported UOM/tender setup, first product/stock and test receipt; clear completion state. | Before self-service launch |
| Existing-shop migration | Catalog, customers, suppliers, opening stock and opening dues; mapping, preview, deduplication, row errors and reconciliation totals. Existing 100-row intake is not a migration/import experience. | Before selling to existing shops at scale |
| Financial correction workflow | Payment receipts with method/reference/allocation, correction/reversal, sale cancellation policy and safely replayable commands. Avoid destructive edits to posted records. | Before real trading |
| Supplier return workflow | Return-to-supplier/debit adjustment and receipt correction tied to purchase documents and stock; preserve sold-lot history. | Before broad stock-heavy retail offer |
| Till/day close | Opening float, cash tendered/change, cash/UPI/card totals, counted cash, variance and responsible cashier; mixed tender if advertised. | Before claiming complete counter management |
| Offline exception console | Per-invoice queue, reasons, authorized resolution, historical lease handling, support bundle and safe device retirement without destroying unsynced invoices. | Before advertising offline continuity |
| Public customer lifecycle | Pricing/plan or explicit free offer, trial/limits, renewal/failure/cancellation, read-only/export policy, support ownership. Automated subscription checkout can follow an auditable manual billing process; a commercial policy cannot be undefined. | Before public paid SaaS |
| Data ownership and exit | Whole-shop export, account/shop closure request, documented retention/anonymization and ownership transfer/recovery. Financial-record retention must be considered before deletion behavior is chosen. | Before public onboarding |
| Help and support | Public contact, help for common cash-counter failures, receipt/network troubleshooting, escalation/status communication, privacy/terms links and support access audit. | Before public onboarding |
| Operational recovery | Restore runbook and drill, per-tenant recovery strategy, alert delivery, incident response, release rollback with schema compatibility. | Before production customer data |

Inventory expiry is another policy gap: expiry fields/alerts exist, but available batches and sale validation do not consistently prevent expired goods from being sold. If targeting food, pharmacy or other expiry-sensitive stock, implement category-specific warnings/blocks/authorized overrides before making that promise.

Actual UPI/card collection, verification and refunds are not established by a payment-method enum. At launch, make clear whether the cashier is recording an externally verified payment or the system is confirming a gateway settlement. Gateway integration is scope-dependent; accurate recorded-payment behavior is essential.

**5. Operations and maintainability assessment**

The deployment work is real. CI builds and tests both applications, scans images, promotes immutable outputs through dev, runs migrations separately, checks readiness and keeps previous image references. Source inspection also confirms production relative `/api`/`/hubs` routing through the web proxy. Earlier infrastructure documents saying these fixes are entirely unbuilt are stale and should not determine readiness.

However, the checked-in infrastructure is expressly sized for a controlled launch: both web and API have minimum zero and maximum one replica. Development and production databases share a PostgreSQL server; no HA configuration is present in that module. Fourteen-day backup retention is configured, which is useful, but configuration is not a successful restore test. None of those choices alone proves the service is unsafe; together they need a measured operating envelope and honest availability promise. [API capacity](/Users/chanakya/projects/intelibill/.tofu/modules/environment-infrastructure/api.tf:35), [web capacity](/Users/chanakya/projects/intelibill/.tofu/modules/environment-infrastructure/web.tf:20), [database](/Users/chanakya/projects/intelibill/.tofu/modules/database/main.tf:8).

The shared-monitoring module creates log/metric collection, but no actionable alert/action-group resources were found there. External New Relic alert configuration was not inspected. Require a real notification test for API failures, auth anomalies, database pressure, failed migrations and offline-sync backlogs. [Monitoring module](/Users/chanakya/projects/intelibill/.tofu/modules/shared-monitoring/main.tf:9).

The release smoke test checks healthy revisions plus `/` and `/api/ping`; it does not establish login, migrated business queries, sale creation, printing or ledger consistency. Image rollback does not undo a database migration. Add an isolated-tenant business smoke and backward-compatible migration/rollback gate. [Smoke script](/Users/chanakya/projects/intelibill/.github/scripts/wait-for-container-app-revisions.sh:70), [migration/rollback ordering](/Users/chanakya/projects/intelibill/.github/workflows/deploy.yml:805).

Exports load complete data and query returns per sale; the PDF row limit is checked after dataset construction. Customer balances also load ledger entries into memory. Test realistic multi-year shops, bounded exports, pagination and simultaneous counters before declaring supported scale. No throughput or latency claim is justified by this review.

Domain events publish after database commit without a durable outbox in the inspected path. That keeps notifications out of the core atomic invariant, but leaves failure/lost-update scenarios. Keep live notifications advisory and refreshable; use durable delivery if receipts, integrations or financial side effects come to depend on them. [Commit/publish boundary](/Users/chanakya/projects/intelibill/src/backend/Intelibill.Infrastructure/Data/ApplicationDbContext.cs:74).

There are additional security hardening items in the API appendix: atomic refresh rotation and token hashing, live hub revocation, atomic throttling/account budgets, generic production error responses, and production email configuration that fails safely. Disabled email currently selects a no-op implementation that logs a full reset URL; actual deployed mail settings were not verified.

**6. Launch sequence and measurable gates**

| Stage | Work | Exit evidence |
|---|---|---|
| A — Establish isolation and identity | R01–R05; safe shop transitions; verified signup/linking; reliable revocation; dependency fixes. | Two unrelated tenants/users, restricted DB role, pooled connections, cached/offline browser, reset/disable/linking attacks all satisfy isolation. Ordinary release build passes without audit suppression. |
| B — Make money and stock trustworthy | R06–R12/R14; atomic bulk rows; batch lineage; replay-safe payments; protected returns; allocation; pricing/reporting invariants. | Deterministic concurrent/retry tests and whole-workflow reconciliation, not just isolated handler tests. |
| C — Complete Indian retail documents | R13; offline seller snapshots/printing; fiscal numbering; supported tax regimes/UOM/expiry behavior. | Accountant-reviewed sample transactions and documents, correct periods, stable reprints, matching exports; unsupported segments gated. |
| D — Complete customer and operator journeys | Setup/import, support, commercial lifecycle, data exit, offline exception console and till scope. | A fresh retailer can start, trade, correct mistakes, recover from outages, get help and leave with their data. |
| E — Prove production operation | Business smoke, realistic load, restore drill, alert delivery, schema-compatible rollback and controlled rehearsal. | Recorded recovery objectives/results, measured capacity, receiving alerts, successful upgrade/rollback and reconciled trading sessions. |

Decisive trading acceptance scenarios:

1. Register and verify → create shop → import products/opening stock and dues → produce a test receipt.
2. Receive a PO partially into multiple lots where supported → complete it → correct or return goods with matching payable/stock movements.
3. Sell goods/services, cash and due → deliberately lose the committed response → retry/reload → exactly one sale, stock movement and receivable.
4. Take part/full customer payment → lose response/retry → exactly one receipt, correctly allocated invoice balances.
5. Two counters sell the last unit; two operators return the same service/wastage line; two sessions redeem one credit note. Each invariant holds and the losing operation has a usable conflict/replay result.
6. Change batch price/cost after some units sold → return an earlier sale → quantities, receipt liability and audit history still reconcile.
7. Enable offline billing → disconnect/expire access token → create/print → reconnect after conflicts and lease boundaries → resolve through the UI without recreating issued invoices.
8. Switch shops/users with nonempty carts, open detail screens, delayed requests and cached responses → no wrong-scope data or draft corruption.
9. Post around midnight IST/month end/fiscal-year boundary; sell in one month, refund in another; compare UI, documents, ledger, P&L and exports.
10. Restore a backup into an isolated environment and reconcile shop stock, receivables, payables, issued documents and outstanding offline work; demonstrate support and customer communication.

For every fixture, assert: opening stock plus signed movement equals closing stock; receipt/adjustment/payment facts explain supplier payable; invoices/returns/collections explain customer receivable; each accounting voucher balances to the cent. Test output counts alone do not prove these cross-feature properties.

**7. What can wait**

Mobile remains outside this release assessment. Advanced demand forecasting, AI assistants, loyalty campaigns, ecommerce integration, enterprise warehouse routing, a general ledger, full GST return filing and universal payment-gateway integration need not all precede a focused retail launch. E-invoicing, inter-shop transfers, complex variants/serial tracking and regulated-stock behavior depend on the selected merchant segment. Restrict that segment explicitly rather than implying support through generic “AI inventory” or “complete accounting” language.

The best next investment is a short, ordered correction backlog tied to the above invariants, followed by real-shop rehearsals. The current product already has enough breadth; reliable handling of identity changes, retries, corrections, period boundaries and outages is what separates it from a publicly supportable SaaS service.
