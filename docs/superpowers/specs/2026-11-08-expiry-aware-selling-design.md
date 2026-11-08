# Expiry-aware selling design

Date: 2026-11-08. Source baseline: `044c1e0c` on `codex/expiry-aware-selling`.

This design implements the supplied expiry-aware selling plan (market-readiness item 34) across API and web. The user authorized analysis, planning, automatic implementation, review/fixes and a PR. The decisions below resolve the proposal's open business choices conservatively; they are implementation assumptions, not evidence of a merchant's approved launch policy. Mobile and regulated-stock compliance are outside this feature. No production policy is enabled by this work.

## Intent and scope

Give a merchant one versioned expiry decision for product/barcode search, lot selection, preview, final posting, offline issuance and inventory handling. Direct API requests must enforce the same rules as the web. Preserve original issued invoices, historical offline evidence, physical stock and recorded-cost accounting. Accept recovered expired customer goods into quarantine without presenting them as sellable.

Include policy administration and readiness reporting, receipt date validation, FEFO, reasoned warning approval, retained historical evidence, quarantine release/disposal, UI/localization and migration/rollback guidance. Do not add a generic override for hard blocks, fabricate missing dates, rewrite issued amounts, or market regulated pharmacy/food compliance.

## Actual source findings

- `InventoryBatch` already has expiry/manufacturing dates and `xmin`. `InventoryBatchRepository` availability/barcode/stream queries filter void/quantity but do not implement expiry; nullable SQL ordering currently puts undated lots first.
- `GetAvailableBatchesQueryHandler` maps physical quantity straight to checkout. `GetProductDetailsByNameOrBarcodeQueryHandler` also uses batches, but supplies intake defaults: preserve its administrative purpose rather than hiding stock from intake.
- `SaleLineValidator` is shared by preview, online posting and the offline poster. Its `allowInsufficientStock` flag is a stock-reconciliation facility, not expiry authorization. Online posting uses a transaction, period fence and till acquisition; customer/credit and batch writes follow. Batch `xmin` protects stock, but no policy revision fence exists.
- Offline catalog schema 3 streams dates but has no authenticated historical expiry decision. `OfflineSalePoster` already uses the claimed issue business date, immutable envelope, arithmetic validator, period and till gates. All initial/recovery/review paths use it. Merely trusting new client policy fields would create a bypass.
- `SaleReturnValidator` rejects expired Restockable lines today. `RecordSaleReturnCommandHandler` restores only Restockable physical quantity; Wastage restores none. Void reverses Restockable quantities. `SaleCancellationService` independently restores quantities and therefore also needs disposition handling.
- Counts, adjustment create/void, supplier shipments and receipt reversals mutate the same physical batch. A batch-wide boolean quarantine would incorrectly hide unrelated good units and cannot support exact return voids.
- `BatchRetailTermsReviser` can edit dates, but its current audit is only a price revision when price changes. Date history must be added explicitly.
- Existing return accounting and cancellation readers reverse frozen recorded COGS. Quarantine is a physical hold, not an automatic write-off; disposal must be a separate dated stock-loss posting.

## Policy decisions

A shop has a default `ExpiryPolicy`; each item may inherit it or have an owner-managed override. All changes append immutable history and advance a monotonic shop `CatalogVersion`, including changes back to inheritance. Default: `Mode=Ignore`, `ExpiryRequired=false`, `SellThroughExpiryDate=true`, `WarningDays=7`, `OverrideAllowed=false`. Policy version starts at 1; a policy edit receives the new catalog version. No merchant category is inferred from names or tax regime.

`Mode` is Ignore, Warn or Block. `WarningDays` is 0..365. `ExpiryRequired` is independent: missing a required date is always a hard block, including under Ignore. Ignore otherwise leaves historical sale eligibility unchanged. For non-Ignore, a dated lot is expired when `businessDate > expiryDate` if sell-through is enabled, otherwise `businessDate >= expiryDate`. Near expiry means a nonexpired date within WarningDays; it is informational, not permission to sell an expired lot.

| Condition | Decision |
|---|---|
| Voided/inactive/no unquarantined stock | Existing stock/item refusal; never overridden |
| Quarantined quantity | Always unavailable, irrespective of policy |
| Required expiry missing | Block `Expiry.RequiredDateMissing` |
| Ignore with date present/optional absent | Allow |
| Block and expired | Block `Expiry.Expired` |
| Warn and expired, overrides disabled | Block `Expiry.OverrideDisabled` |
| Warn and expired, overrides enabled | Eligible only with live Owner/Manager approval and reason |
| Nonexpired within warning window | Allow with `Expiry.ExpiringSoon` warning |

The pure evaluator returns eligibility, structured reason/warning, whether approval is required, policy/catalog version, evaluation business date and physical/unquarantined/available quantities. An eligible warning lot is not permission to post without the required approval. Services have no expiry policy. No automatic batch substitution is performed.

## Policy administration and concurrency

Add `/api/expiry-policies` read/default update, `/api/expiry-policies/items/{itemId}` read/update override (null policy means inherit), and `/api/expiry-policies/readiness` for missing-date/expired/quarantine totals and affected lots. Reads require current membership; writes require Owner, nonzero expected catalog version and a 1..500-character reason. Readiness is available to Owner/Manager. Any policy change, including weakening a rule, is explicit and audited. Ordinary item updates cannot smuggle policy changes.

Use a dedicated `shop_expiry_states` row. The migration seeds existing shops with an explicit known pre-feature Ignore baseline; `CreateShopCommandHandler` inserts new-shop state and its initial immutable revision inside the existing tenant-write-scope transaction before the shop commits. Missing state/history at runtime is an error, never an opportunistic Ignore default. Shared policy reads inside a posting transaction acquire `FOR SHARE`; policy/date edits acquire `FOR UPDATE`, reload and compare expected version. Restore lock_timeout; a 5-second timeout returns `Expiry.PolicyBusy`. The fence is taken after the posting-period fence and before till/party/document/stock locks. Policy administration takes only the expiry fence before any item/date row. No caller may acquire it after locking a batch. Recheck live authority before new policy/disposition writes and before replaying committed operations.

This makes a posting see one committed policy/date revision while allowing concurrent sales. Policy/date administration waits for existing postings to finish. Take snapshot policy/date facts consistently under the same fence and finish persistence before publishing evidence. Record actual effective timestamps from the server clock. Global catalog invalidation is intentionally conservative: an unrelated policy edit can require a fresh preview.

## Online search, approval and posting

FEFO order within item is eligible dated lots by expiry, undated eligible lots last, then batch number and batch ID. Default `/inventory/batches/available` excludes hard blocks and zero eligible quantity. An opt-in `includeUnavailable=true` diagnostic projection returns blocked candidates with reason and zero available quantity, allowing the picker to explain a barcode whose only lots are blocked. Never expose another shop's stock. Administrative inventory lists continue to show physical stock, quarantine and effective available stock separately.

Use the same evaluator for availability, preview and final posting against `IBusinessClock` and the shop time zone. Preview returns line decisions and warning approval requirements. It does not create approval facts. A post carries optional `ExpiryApprovals`, one per batch, binding batch ID, summed quantity across duplicate lines, batch xmin, policy version, catalog version and reason. The server loads the actor's live role, reloads current facts under the expiry fence and checks every binding. Client booleans, actor IDs and supplied evaluation dates are not authority. Empty/stale/extraneous/duplicate approval entries fail explicitly. A hard block always wins.

Freeze per-line `SaleExpiryDecision` evidence with sale/item/batch identity, quantity, dates, policy settings/version, business date, result, and approved actor/reason/time when applicable. Store separately from the issued invoice document, preserving document-v2 and offline-arithmetic-v1 contracts. Include approvals in new sale idempotency hashes but preserve the exact historical hash shape when the optional field is absent. Replay an already committed sale after authorization without reevaluating today's expiry.

## Receipt dates and date correction

All new receipt entry paths (single batch, bulk row, inbound merge, PO receiving) reject manufacturing after expiry. Required expiry is checked under the effective item policy before row writes. Retain bulk per-row commits: a failed row writes no stock/payable/receipt, while independent successful rows retain the existing result semantics. PO receipt groups remain atomic.

Every date change requires nonzero expected batch xmin (including the legacy update route). `UpdateBatchPricesCommandHandler` supplies its existing Reason; `UpdateInventoryBatchCommandHandler` supplies existing Notes as the audit reason. Price-only edits retain their existing reason rules. Every date change through retail-term or legacy batch update appends `BatchExpiryRevision` with old/new dates, actor, time and required reason, then advances CatalogVersion under the exclusive fence. Reject invalid date order. Historical sale/offline/disposition facts keep their original dates. Editing a date never clears quarantine or constitutes release. Existing invalid/missing dates are reported, never fabricated or rewritten by migration.

## Trusted offline decisions

New catalog schema 4 adds shop time zone/catalog version, per-lot effective policy, physical/quarantine/available quantities and a server-generated `expiryEvidenceId`. Persist an immutable `OfflineExpiryEvidence` row for each published lot containing shop/snapshot/batch/item IDs, captured dates and policy, catalog version, captured available quantity, issued-at and expiry (`issued-at + 7 days`). The row is authority; the client copy is display/calculation input. Evidence is persisted before its record is yielded. Use bounded pages so the streaming reader and writes do not share an active PostgreSQL reader. Completed catalogs alone become active on the device.

At finalization the device evaluates the issue business date in the stored shop zone, requires schema-4 evidence for newly configured policies, and refuses offline finalization of a line requiring warning approval. Live server approval cannot be established while disconnected. Freeze evidence IDs and exact decision inputs in the queued invoice; synchronization/recovery/printing never decorate old envelopes with current data. Keep all earlier schema readers for already-issued invoices.

The shared poster resolves each evidence ID by shop and batch, verifies the retained server payload against all client decision fields, verifies capturedAt <= SoldAt <= evidence expiry, and uses the existing lease/issuer/timing validation. Evidence is catalog authority, not user authorization: the invoice lease, authenticated attribution and existing issuer-standing gates still determine who issued it. Evaluate the immutable server policy/date facts on the issue business date. Check revision history: if a relevant policy/date change became effective before the claimed issue, stale evidence needs review; a change or expiry after issue must not invalidate that supported historical decision. A missing/tampered/expired/mismatched proof, unknown historical policy or forged warning approval produces a held `ExpiryReview=14`, never a silently repriced/rejected-and-forgotten invoice.

Legacy envelopes with no new fields retain the historical path only when server history proves the effective policy was the pre-feature Ignore default at issue and expiry was not required. A shop that later switched back to Ignore cannot bypass unknown history. Present but incomplete new fields never fall through to legacy compatibility. No artificial policy history is invented for an interval the server cannot establish.

ExpiryReview actions are explicit: Escalate for the existing eligible actor; LinkExistingSale only for the exact envelope identity/hash and a posted sale whose retained expiry decisions pass the same trusted authority checks; RetryHistoricalLease only with complete retained evidence and supported attribution; ReconcileTillSale only when those expiry checks already pass and the existing till-review conditions allow it. LinkStockCorrection and AcknowledgeVariance are never offered for ExpiryReview. Execution rechecks all conditions even if the UI once offered an action. Manager reason alone cannot override a hard block, mark an unresolved expiry decision reconciled, or manufacture missing authority. No generic 'post anyway' action is added. A separate till/period review can still hold the invoice; neither bypasses expiry. Current stock consumption uses only unquarantined stock, with the established stock-variance behavior for deficits; an old valid invoice cannot consume today's quarantined units. Once posted, exact-envelope retries return its existing outcome.

Future-use local catalog caches become stale after policy/date edits and must refresh when connected; already-issued envelopes and their evidence are retained. Disconnected devices cannot learn a remote policy edit instantly: stale issue-time authority is conservatively held on sync. This is an operational limit to explain, not a guarantee of instantaneous offline revocation.

## Physical stock and disposition

Add `InventoryBatch.QuarantinedQuantity` default 0 with `0 <= quarantined <= Quantity`, and computed `UnquarantinedQuantity = Quantity - QuarantinedQuantity`. `Quantity` and aggregate `Inventory.Quantity` retain their physical meaning. The evaluator further gates the unquarantined amount by expiry. New mutable methods must preserve both quantities; generic sale subtraction cannot draw from quarantine.

Recovered Restockable customer goods are accepted even when expired. The server computes the restoration disposition on the return business date: expired by the configured inclusivity (also when Mode Ignore), or missing a required date => physical +q, quarantine +q, ordinary return stock movement +q, and an immutable quarantine event. Other Restockable goods add physical only. Explicit Wastage remains the existing non-restored disposal choice; services remain refund-only. Preview and saved DTOs explain effective disposition. Freeze quarantined quantity and the cause/date/policy facts on each returned line. Cancellation recovery uses the same rule, with its existing eligibility/settlement unchanged.

`BatchDispositionChange` is an immutable append-only ledger. A root quarantine event names source kind/ID/line, batch, quantity, frozen expiry/cause, actor, business date, timestamp and stock movement. Subsequent release/disposal/reversal events reference that root. Root remaining quantity is derived from signed events; no deletion or mutable historical relabeling. The sum of root balances equals the batch quarantine field.

Add owner-only `POST /api/inventory/batches/{id}/dispositions` with `{operationKey, expectedVersion, quarantineChangeId, action: Release|Dispose, quantity, reason}` and a matching operation-result lookup. Use new financial operation type 27 and the existing durable executor; return saved outcome after authorization even if current policy changed. Both actions lock period -> expiry -> batch/item after the operation reservation. Fractional quantities follow item scale; validate the targeted root belongs to the shop/batch and has enough remaining quantity.

Release changes quarantine -q, physical unchanged and writes only a disposition event. It is allowed only for a root not already expired when quarantined, with a currently nonexpired valid date and no current hard block/approval requirement. An originally expired root cannot be released by changing its expiry date. A missing-date root may be released after a separately audited genuine date correction. The endpoint itself cannot edit expiry. Disposal changes physical -q and quarantine -q, writes a stock-loss movement linked to an `InventoryAdjustment`, and retains original recorded batch cost; it has no generic void because that would resurrect disposed goods. Source links must prevent ordinary adjustment-void routes from undoing a disposition disposal.

Return void reverses the exact original disposition, never today's policy: physical -q and original quarantine -q, with a linked reversal event. If the root was subsequently released/disposed, refuse the void rather than taking unrelated units. Nonquarantined restored units can be reversed only from unquarantined stock. Cancellation has no uncancel. Counts cannot set physical below quarantine or silently clear quarantine. Ordinary adjustments and supplier shipment consume unquarantined stock only; explicit quarantine disposal is the route for held units in this release. Supplier/receipt reversals refuse source lots with quarantine history. Adjustment void restores the original recorded disposition, not a new sellability assumption. Inventory changes never delete the journal.

Quarantine itself is a physical inspection hold, not an automatic impairment. Existing Restockable return and cancellation COGS reversal occurs once. Physical stock valuation retains recorded cost and reports quarantined quantity/value distinctly; it does not claim recoverability. A later explicit disposal recognizes independent stock loss once on its own date. Existing Wastage return accounting remains unchanged. Test export/P&L/cancellation consistency; do not add the quarantine event itself to independent loss.

## Web behavior

Owner shop settings include default policy and item editor includes inherit/override. Show effective policy/version, readiness counts and explicit audit reason before saving. Never change existing shops during UI load. Show expiry date, localized status text and available versus physical/quarantine quantity in inventory/lot picker, with accessible labels rather than color alone. Blocked candidates cannot be added. FEFO is deterministic, and carts recheck on preview/posting and local business-date rollover.

For warning approval, live Owner/Manager checkout presents affected lots, quantities and a required reason; Staff sees that manager approval requires the manager's own session. Freeze the bound approval request in the existing checkout intent before HTTP; unknown outcomes retain their key/payload. Editing a cart or refreshing a stale preview invalidates unsubmitted approval bindings. Submitted unknown intents remain immutable and recover through the same idempotency key.

Bulk/single receipt UI marks required expiry and inconsistent date pairs. Return preview explains physical restoration to quarantine; inventory exposes root disposition history and owner release/disposal with durable scoped drafts. Use the established IndexedDB owner/shop/action/key claims, version conflicts and unknown-404 recovery, not localStorage-only ad hoc drafts. Offline status explains refresh/online-approval needs and ExpiryReview without altering already-issued invoice identity. Add locale keys to every shipped locale; preserve existing till selectors and settlement evidence.

## Persistence, rollout and verification

Migration adds policy state/history, batch-date history, offline evidence and disposition tables with explicit RLS/tenant-policy manifest entries, composite tenant foreign keys where existing patterns require them, and indexes for shop/version/batch/snapshot/source lookups. Add immutable sale decision JSON and return/cancellation disposition columns; existing rows default to no decisions/zero quarantine. Seed only the compatible default state. PostgreSQL constraints enforce quantity ranges and bounded enum/config values. Migration Down must refuse destructive removal of retained evidence.

Deploy schema, every writer/reader and then web before an owner enables a policy. Run readiness report per merchant, verify dates and confirm category choices outside this development task. Do not enable a global regulated policy. Rollback uses a compatible forward fix or explicit owner policy change; retain schema/evidence/quarantine, and never run pre-feature writers after new facts exist. Turning Mode to Ignore does not release quarantine or erase approvals.

Prove pure evaluator parity; real-PostgreSQL role/tenant isolation, policy-edit/posting serialization, exact replay and quantity invariants; issue-before-expiry/sync-after-expiry; forged/legacy evidence; return/void/cancellation/disposal accounting; and web durable-intent/accessibility/localization behavior. Run required full build/test/release checks after focused regressions. Tests are evidence only when their process exits and assertions pass, not when a provider reports success.
