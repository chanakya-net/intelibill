# Shop affiliation and consent

A billing Owner or Manager opens Shop coverage and selects a merchant shop ID and one coverage choice. The account member shares the request's Shop owner consent link. The link addresses a request. Merchant verifies the signed-in session and checks that the caller owns the selected shop before displaying the account and its coverage choice.

The request fixes the account, shop, selected term and seven-day expiration. The owner grants consent, then separately accepts or revokes it. Merchant locks the user, session, shop and membership and checks the live owner and membership revision again when recording each decision. An accepted decision is durable at the merchant commit. The acceptance event is also the pending outbox record. Revocation and ownership changes take the same shop lock. They invalidate grants that have yet to be accepted.

A delivery worker sends the committed decision with a separate service assertion. Licensing checks the recorded request, the requester's billing authority and the selected term. It commits the immutable decision receipt and any affiliation together. The global shop key allows only one affiliation. A term must be current at application and have capacity for the named shop. An assignment starts at application and keeps the existing term end. Independent terms retain their own dates. A selection that becomes unavailable reaches **Needs review**. A lost acknowledgement leaves the same event pending until its result is recovered.

Both databases retain their own facts. The merchant writes no membership or business record in this flow. Coverage here is observe-only. A full term, an expired term and an affiliation without coverage convey no additional operating authority.

## Local setup and verification

Apply the merchant and licensing migrations through their normal migration jobs. The merchant worker uses the existing licensing origin. Configure its distinct signing key under `ShopAffiliationDelivery` using `KeyVaultKeyId` or the local-only `LocalKeyId` and `LocalPrivateKeyPem`. Configure the matching public keys in licensing under `ShopAffiliationDelivery:PublicKeys`. With delivery unconfigured, accepted owner decisions remain pending. Licensing rejects service assertions signed by an interactive billing key.

The Chromium fixture seeds two independent terms and one grouped term in local PostgreSQL. Select the grouped term, open the consent link as the fixture shop owner, grant consent, and reload before acceptance. The fixture drops the first acceptance response and retries with the saved key. It verifies one visible affiliation with unchanged independent terms. It also exercises revoked consent and checks that the payer's shop list stays unchanged and direct shop read and switch are refused. The fixture saves its observations and desktop and mobile screenshots in the local test evidence directory.

Behavior-first red check against `3544c9c6cba46dadaec51480a00263a70c8c4ee1`, with only the initial test added:

```shell
dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0205Tests' --logger 'console;verbosity=normal'
```

The test ran on PostgreSQL and failed at `RequestAsync`. It expected HTTP 201 Created and got HTTP 404 Not Found. The exit status was 1.
