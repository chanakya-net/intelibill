# Shop trial activation

A shop owner opens **Start a shop trial** from Billing access status. Preview reads the current published terms, member limit, concurrent trial-shop cap and prospective end in IST. Setup and preview consume no trial. Active members include the owner. Existing shop permissions continue to apply.

The confirmation sends the exact policy identifier, revision and terms version with an operation key. Merchant locks the live session and shop authority, then records the owner's consent and the active member count in its database. Billing stores the operation in the browser before sending it. Reload recovers using the same key. A pending operation displays its proposed deadline. A completed refusal can be retried after its cause is corrected and the owner confirms again.

Merchant delivers the retained consent with a distinct service assertion whose signature binds the HTTP path and body. Licensing takes the account and stable shop locks before applying the decision. It checks the current published policy and considers every overlapping trial interval under the account cap. One committed trial creates one independent coverage term and a retained history row keyed by the merchant shop ID. The term is reserved for its originating shop. Shop names, owner identities and email addresses do not define eligibility. The stable shop ID remains the history key if its account changes. Newly created shop IDs require separate evidence to identify as the same real-world business.

The window starts at recorded consent and ends exactly 336 hours later. The end is exclusive. Retrying or delaying delivery preserves the original window. Trial coverage has no payment, renewal or grace. Merchant licence status stays observe-only.

## Local verification and migrations

Apply merchant migration `20261116160000_AddTrialActivationConsents` and licensing migration `20261116160000_AddShopTrials` before deploying the corresponding writers. Each database refuses to roll back its trial migration once retained evidence exists. A code rollback can retain the tables.

Local fixtures use `CataloguePublication:Mode=LocalFixture` with a complete published trial policy and a dedicated `ShopTrialDelivery` key pair. The merchant uses the existing licensing origin. The licensing host verifies the named public key. Local keys and fixture provenance stay in local environments.

Build the merchant web from `src/frontend` with `bun run build`. From the repository root, run the P0210 browser integration test with `P0210_RUN_BROWSER=1` and `P0210_EVIDENCE_DIR` set to an artifact directory. The harness uses loopback hosts and Testcontainers PostgreSQL and invokes Playwright in Chromium against the shipping build. It captures the explicit consent, request counts, recovered operation, persisted cap and window, exact expiry and an observe-only stock posting.
