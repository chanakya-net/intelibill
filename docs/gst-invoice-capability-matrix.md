# GST Invoice Capability Matrix

Status: **awaiting accountant approval** before GST documents are enabled for public merchants. These
are product scope choices for the documents Intelibill issues, not statutory claims. Rule text was
cross-checked on 2026-10-21 (see the market-readiness review); revalidate amendments before approval.

## Seller profile (declared by the shop owner)

| Regime | GSTIN | E-invoice attestation | Documents issued |
|---|---|---|---|
| Not declared | — | — | None: sales and offline ranges are refused |
| Regular | Required, checksum-valid, prefix = state | Below threshold or Exempt | Tax invoice / Bill of supply / Invoice-cum-bill of supply |
| Regular | Required | Required (IRN applies) | None: refused until an IRP integration exists |
| Composition | Required, checksum-valid, prefix = state | — | Bill of supply, intrastate only, no tax |
| Not registered | Not allowed | — | Receipt, no tax |

## Worked examples (seller in Karnataka, 29)

| # | Case | Lines | Document | Tax components | Total |
|---|---|---|---|---|---|
| 1 | Regular, walk-in, counter sale | ₹100 taxable @ 18% | Tax invoice | CGST 9% ₹9 + SGST 9% ₹9 | ₹118 |
| 2 | Regular, place of supply Tamil Nadu (33) | ₹100 @ 18% | Tax invoice | IGST 18% ₹18 | ₹118 |
| 3 | Regular seller in Chandigarh (04) | ₹100 @ 18% | Tax invoice | CGST 9% ₹9 + UTGST 9% ₹9 | ₹118 |
| 4 | Regular, odd paisa | ₹0.28 @ 18%, tax ₹0.05 | Tax invoice | CGST ₹0.03 + SGST ₹0.02 | ₹0.33 |
| 5 | Regular, mixed, unregistered buyer (Rule 46A) | ₹100 @ 18% + ₹50 @ 0% | Invoice-cum-bill of supply | CGST ₹9 + SGST ₹9 on ₹100; ₹50 exempt | ₹168 |
| 6 | Regular, all exempt | ₹50 @ 0% | Bill of supply | — | ₹50 |
| 7 | Regular, registered buyer (GSTIN 29…) | ₹100 @ 18%, HSN 8471 | Tax invoice with buyer name, address, GSTIN, state | CGST + SGST | ₹118 |
| 8 | Regular, registered buyer in Tamil Nadu, counter sale | ₹100 @ 18% | Tax invoice | CGST + SGST (place of supply is the counter) | ₹118 |
| 9 | Composition | ₹80 @ 0% | Bill of supply + "composition taxable person, not eligible to collect tax on supplies" | — | ₹80 |
| 10 | Not registered | ₹80 @ 0% | Receipt + "not registered under GST" | — | ₹80 |
| 11 | Regular, service to a registered buyer in Tamil Nadu | ₹100 @ 18%, SAC 998719 | Tax invoice, place of supply 33 (the recipient's location) | IGST 18% ₹18 | ₹118 |
| 12 | Regular, service to an unregistered buyer, no state on record | ₹100 @ 18% | Tax invoice, place of supply 29 (the seller's location) | CGST + SGST | ₹118 |

Place of supply, when the cashier does not state one: goods handed over at the counter are supplied
in the seller's state; services go to the recipient's recorded state (the GSTIN's for a registered
buyer), else the seller's. A stated place of supply is used as given.

## Refused (no number is taken, nothing posts)

| Case | Code |
|---|---|
| Profile not declared | `Invoice.TaxProfileRequired` |
| E-invoice required | `Invoice.EInvoiceUnsupported` |
| Composition or not-registered seller charging any tax | `Invoice.CompositionCannotCollectTax` / `Invoice.UnregisteredCannotCollectTax` |
| Composition interstate supply | `Invoice.CompositionInterstateUnsupported` |
| Place of supply outside the domestic codes (96 export, 97 other territory) | `Invoice.PlaceOfSupplyInvalid` |
| Registered buyer without name or address; unregistered buyer without name, address and state at ₹50,000 taxable or more | `Invoice.BuyerDetailsRequired` |
| B2B line without a 4/6/8-digit HSN (below threshold), any line without 6/8 digits (exempt registration) | `Invoice.HsnRequired` |
| Line or sale amounts that do not add up | `Invoice.AmountsDoNotReconcile` |
| Taxable and exempt items together for a registered buyer (Rule 46A is for unregistered recipients only) | `Invoice.MixedSupplyToRegisteredBuyer` |
| Goods and services together whose places of supply differ (services for an out-of-state buyer), none stated | `Invoice.PlaceOfSupplyRequired` |

Offline sales already handed over on a device always post; if their profile refuses them they post
without a document and open an informational `DocumentReview` case.

## Out of scope

IRN e-invoicing and B2C dynamic QR, exports and SEZ supplies, reverse charge, cess, special valuation,
per-item unit of quantity (all goods print `NOS`), device-side GST printing, credit-note documents.

## Approval

| Reviewer | Date | Decision | Notes |
|---|---|---|---|
| | | | |
