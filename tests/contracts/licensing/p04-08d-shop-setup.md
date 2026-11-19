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

Detail, setup status, sample and preference reads require the caller's live session and an active membership in the requested shop. They load protected data inside the transaction that holds the shared licence fence, so a security suspension either commits first and refuses the read, or waits until the read finishes. Expiry policy writes hold the actor's account row through commit, so sign-out, rescope and role changes serialize with them.

Setup writers and additional-shop creation wait at most three seconds for the actor, shop and membership rows while the licence fence is held. Contention answers 409 `Licence.Busy`, writes nothing and releases the fence, and the same request can be retried.

In the merchant web app, a licence refusal from any shop setting or sample request applies to that shop. A security refusal removes its details, receipt and print action. A trading refusal withdraws operating permissions until a fresh read. A detail read that started before either refusal cannot restore them. Members other than the owner keep the read-only tax profile.

Setup status, sample and preference reads describe the shop the calling device is in. The active shop comes from the validated session, so a direct caller that names another of its shops receives `ShopSetup.Forbidden`. Selected-shop details stay available to an active member of that shop.

Each protected read checks the device, the member's role and the licence decision again after its data has loaded. A device revoked or a membership withdrawn in the meantime receives `Auth.SessionInvalidated` or `Shop.MembershipNotFound` with no shop data, and a role that changed answers 409 `Licence.Busy` for a retry under the current role. The returned `setupAccess`, setup permissions and `canSavePreferences` come from the decision at the end of the read. A read that crosses the trading end reports `Expired` with the recovery fields and no operating permissions, and the authorized sample stays readable.

In the merchant web app, a profile, tax or bank save that answers after a licence refusal for the same shop completes without republishing that shop's details or permissions. A fresh read can restore them, and other shops are unaffected. A security refusal from an expiry or till settings read is handled like one from a save. A security refusal of the checklist's preference save withdraws the checklist and its actions before the follow-up read answers.

A security refusal in Manage Shop also withdraws the mounted checklist and its Start billing action, and the same owner's sample receipt and Print action in another open tab. Closing Manage Shop leaves those views withdrawn until a fresh authorized setup or sample read. An outstanding preference save cannot navigate or restore the view. A refusal for a different shop leaves the current view available.
