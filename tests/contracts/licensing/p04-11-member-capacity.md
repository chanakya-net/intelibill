# P04-11 active-member capacity

A seat is one `Active` membership (Owner, Manager or Staff) in one shop. A person who is Active in two shops holds one seat in each. Pending invitations, Suspended and Removed memberships hold no seat.

| Transition | Seat rule |
| --- | --- |
| `POST /api/invitations/{id}/accept`, no membership or a Suspended/Removed one | Consumes a seat. Refused at the allowance with 409 `Licence.MemberCapacityReached` and no usage figures. The membership is not created or changed and the invitation stays pending, so the same link works once a seat is free. Wrong account, token and inviter-authority checks still answer first. |
| `POST /api/invitations/{id}/accept`, already Active | Idempotent, as before. No count. |
| `PATCH /api/shops/{shopId}/members/{userId}`, Suspended/Removed → Active | Consumes a seat. Refused at the allowance with 409 `Licence.MemberCapacityReached` and `memberCapacity { memberAllowance, activeMembers }` (owner-only route). Status, role and `version` are unchanged. Own-membership, owner-row (`Users.CannotModifyOwner`) and `expectedVersion` checks answer first. |
| `PATCH` Active → Active (role only), any move to Suspended/Removed, Suspended ↔ Removed | No count. A status-only removal or suspension (role unchanged) is `MemberRemoval` and stays available in every licence phase; leaving Active frees a seat. A role change bundled with a status change stays `OperationalCapacity` (`MembershipLicenceActions.For`) and keeps the existing restriction gate. |
| `POST`/`DELETE` invitations | No count. Issuing invitations beyond the allowance is allowed; only acceptance is gated. |
| `POST /api/shops` | Bootstrap: seats exactly one Owner before any entitlement exists for the new shop, so nothing is counted. A later projection may carry an allowance or not: a supplied allowance is always positive (`ShopEntitlementSnapshotV1.IsValid`), and an unknown (null) allowance blocks every further enrolled admission with 503 `Licence.VerificationUnavailable` while removal stays available. The creator's seat in the source shop is untouched. |
| A later revision with a lower allowance | Nobody is removed or demoted. Admissions are refused until the owner suspends or removes enough members. |

## Decision (`MemberSeatPolicy`)

Evaluated on `ShopLicenceLease.CurrentDecision()` at the admission instant (time may pass while waiting for locks):

1. Not enforced (no projection, or `enforcement_activated_at` null): allowed, nothing counted.
2. Enforced and not operating: that phase's refusal (`Licence.PendingActivation`, `Licence.Expired`, `Licence.TradingRestricted`, `Licence.SecuritySuspended` 403, `Licence.VerificationUnavailable` 503).
3. Enforced, operating, `member_allowance` null: 503 `Licence.VerificationUnavailable`.
4. Enforced, operating, Active count ≥ allowance: 409 `Licence.MemberCapacityReached`.

## Locking

Licence fence (shared advisory; account then shop) → `users` rows ascending (the calling device session is revalidated here, since revocation takes the same account lock: 401 `Auth.SessionInvalidated`, no usage figures) → `shops` root `FOR UPDATE` → invitation → membership rows → invitation deadline rechecked at a fresh decision instant (400 `Users.InvitationNotAcceptable`, which is also the recorded `AcceptedAt`) → Active count → write. The fence keeps the allowance stable for the transaction, and the shop root lock serializes every seat-consuming transition in the shop. The fence requires READ COMMITTED, so the count after the shop lock sees the previous admission's commit. Two concurrent final-seat acceptances, reinstatements or a mix of both admit exactly one. No new lock, table or migration.

## Evidence and enablement

`P0411LicensingTests` cover races, boundaries and authority ordering on real PostgreSQL with a controlled clock. Enforcement is switched on per shop by test SQL only. `licensing-member-capacity.spec.ts` covers the accept page and the membership editor in Chromium on built assets, using local HTTP fixtures (not a live API). Production enrollment stays disabled.
