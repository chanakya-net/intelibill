# P03-06 manual full-payment acceptance

Use an accepted fixture order and its billing account ID. Sign in to the shipping Provider Console as an invited administrator with Billing. Open Manual payments, load the accepted order, and enter a single bank reference, evidence reference, reason, IST received time and the exact gross paise amount. Preview the whole package and review every frozen shop page. Attest that the receipt was verified against receiving-account evidence and confirm with recent MFA.

The receipt records Manual, Verified, the immutable actor and timestamps, configuration/policy and the fulfilment disposition. Access initially shows Applying. After signed merchant acknowledgements it shows Applied. A separate full receipt for an already fulfilled order stays Verified with DuplicateFunds for review. An online attempt retains its original provider pin, status and recovery key. A lost response can be checked or retried with the original browser-saved confirmation across reloads.

The receiving-account policy is disabled by default. The configured ManualPayments section requires Enabled, ReceivingAccountId, Label, Environment, ConfigurationVersion, EvidencePolicyVersion, ReferencePattern, EvidencePattern and a protected base64 ReferenceKey (32 bytes). Reference uniqueness uses trimmed, uppercased text within a stable receiving account and environment. Keep that identity and key stable across configuration version changes and retain the purpose-bound Data Protection key ring for operation replay. Use a safe account label and reference formats approved for the actual receiving account. Apply the licensing migration and roll out both updated readers before enabling confirmation.

Browser evidence uses a synthetic receiving account, enrolled local administrators, shipping admin assets, local APIs and independent PostgreSQL databases. Browser evidence includes no credentials or raw bank evidence. The harness binds the enrolled fixture identities to the shipping server's loopback origin. It starts the provider fixture clock five minutes behind, then returns it to its normal time to exercise stale MFA and a second real TOTP. Test results make no claim about live receiving-account or provider verification.

```sh
dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0306Tests' --logger 'console;verbosity=normal'
P0306_RUN_BROWSER=1 P0306_EVIDENCE_DIR=/absolute/local/evidence dotnet test tests/backend/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0306BrowserTests.FinanceConfirmsFullManualReceiptAndRecoversLostReply' --logger 'console;verbosity=normal'
```

Build shipping admin assets with `cd src/admin-frontend && bun run build` first. The harness invokes `bun x playwright test --config ../../tests/backend/licensing/browser/playwright.p0306.config.ts` from that directory and checks PostgreSQL counts after Chromium exits.
