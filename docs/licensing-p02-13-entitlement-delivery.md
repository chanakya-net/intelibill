# Entitlement snapshot delivery

Licensing saves a frozen operational snapshot in the same PostgreSQL transaction as an accepted affiliation, trial activation, trial extension, or complimentary grant. Each snapshot carries a shop revision and an independent account restriction revision. A shop renewal can advance its own revision while the merchant retains a newer account restriction. Releasing a restriction requires a signed release referencing its revision. Phase 2 has no restriction administration.

The merchant authenticates each request, binds the snapshot to retained owner consent, and commits its inbox receipt, verified evidence, projection and acknowledgement outbox together. The licence service matches acknowledgement identity, input revisions and digest against its frozen snapshot. An HTTP response to the snapshot is only a delivery receipt. The separately signed acknowledgement establishes the recorded processing outcome. Older events can be acknowledged as superseded without changing a current projection.

Both workers claim short PostgreSQL leases before making HTTP calls and fence completion by lease token. Retries send the same bytes with a new short-lived assertion. Pending acknowledgements retry with capped backoff. Exhausted events retain a `DeadLetter` status and failure code. Use the durable outbox and receipt to identify the affected event before retrying or requesting a fresh reconciliation.

The source worker scans existing affiliations and due selection boundaries. Merchant reconciliation sends affiliation identity and the two known revisions. Licensing re-evaluates both scopes under the existing account fence. Merchant diagnostics stop asserting permission at the earliest known deadline, the next source boundary or 48 hours after either required scope was verified. Retries preserve upstream verification time.

## Local integration setup

1. Apply `20261116200000_EntitlementDelivery` in the licensing database and `20261116200000_AddShopLicensingState` in the merchant database with the explicit migrator jobs.
2. Run the runtime grant steps after migrations. Each runtime can execute its own bounded claim, completion and reconciliation scan functions. Both services retain separate database users and connections.
3. For a Development or Testing fixture, configure `EntitlementDelivery:Enabled`, `PeerOrigin`, `LocalKeyId`, `LocalPrivateKeyPem` and `PublicKeys:<peer-key-id>` separately in each host. The two keys must be distinct from each other and from merchant JWT, delegated billing and consent-delivery keys. The receiver allows only its configured purpose, canonical path and exact signed body. Remove a rotated key from active trust and retain its public material under `RetiredPublicKeys` if desired. Accepted evidence also retains its public verification key.
4. The transport defaults to disabled. HTTPS is required beyond Development or Testing loopback. Key Vault signing uses dedicated RSA keys and managed identities. Licensing delivery takes a versioned key ID. Key Vault permissions and actual deployment verification are outside the local fixture checks.

`EnforcementActivatedAt` is merchant-owned and remains null for newly applied projections. Snapshot delivery never gates commerce. [P02-14 synchronization status](licensing-p02-14-synchronization-status.md) shows when the merchant confirmed the saved revisions. Retain both schemas and their evidence on application rollback. The migrations refuse downgrade after delivery facts exist.

The versioned wire fields are recorded in [the contract fixture](../tests/contracts/licensing/entitlement-snapshot-v1.json). `P0213Tests` runs against real PostgreSQL with fixed lifecycle and lease clocks. The opt-in `P0213BrowserTests` uses the shipping merchant and provider builds, real sign-in, local signed transport and sanitized browser and persistence evidence.
