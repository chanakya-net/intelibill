# P04-08d merchant shop setup

`GET /api/shops/{shopId}` and `GET /api/shops/{shopId}/setup` include `setupAccess` with contract version 1 and the requested `shopId`. Shop profile, tax profile and shop bank writes also return the hint for that shop. Callers can display `status` and `enforced`, and intersect the named `writableProfileFields` and action booleans with their current UI context. A missing hint conveys no operating permission.

| Request | Enrolled pending owner | Enrolled operating owner | Enrolled expired, trading-restricted or closed owner |
| --- | --- | --- | --- |
| Profile PUT | Profile and contact fields | Existing validation | Trade/display name, contact person and mobile number |
| Tax profile PUT | Existing validation | Existing validation | Refused |
| Setup preferences PUT | Versioned setup preference | Versioned setup preference | Refused |
| Bank details or bank account writes | Refused | Existing validation | Refused |
| Till configuration and shop/item expiry rules | Refused | Existing validation | Refused |
| Additional shop creation from this source | Refused | Allowed subject to live owner authority | Refused |

Profile PUT keeps its existing full-input contract. The server compares normalized protected values against the current locked shop and refuses the entire mixed write. GSTIN omission retains the declared profile's GSTIN under the existing domain rule. A first shop is created from a live shopless session only when the actor has no merchant membership history. A concurrent or previously affiliated shopless actor receives `Shop.SelectExistingShop`.

Security suspension blocks affected setup and profile access. Existing billing and identity endpoints continue to enforce their own authorities. The setup checklist's `canTrade` describes saved prerequisites. The setup UI also requires `hasOperatingAccess` before offering a new sale. Each source transaction makes the final decision. An owner can leave the checklist without saving a preference.

The policy uses local merchant projections with existing enforcement enrollment. Setup writes create no coverage, activate no trials and confer no posting authority. The release browser spec uses local API fixtures, while `P0408dLicensingTests` exercises merchant HTTP and PostgreSQL separately.
