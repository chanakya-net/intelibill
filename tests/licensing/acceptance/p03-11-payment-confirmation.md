# P03-11 acceptance

Run `P0311Tests` against real PostgreSQL. The initial behavioral regression expected an attachment-capable confirmation route after verified simulator capture and failed with 404 before production edits. Merchant and admin UI regressions failed on the missing controls. Keep red/green logs outside source.

Build merchant and admin shipping apps. Run `P0311_RUN_BROWSER=1 P0311_EVIDENCE_DIR=<absolute external evidence directory> dotnet test tests/licensing/integration/Intelibill.Licensing.Integration.Tests.csproj --filter 'FullyQualifiedName~P0311BrowserTests' --logger 'console;verbosity=normal'`.

The fixture creates real merchant identities, billing accounts and an accepted quote, captures through the development simulator and verifies through real APIs. Chromium signs in, downloads an authenticated attachment, then signals the fixture to transfer billing ownership, edit merchant profile/shop details, retire/publish catalogue facts and acknowledge delivery. It reloads and downloads identical bytes. An authenticated request under another owned account returns 404. A live Billing operator downloads the same bytes. Desktop/mobile screenshots check visible layout; post-login videos record both flows. `observable-demo.json` records HTTP observations; `browser-postgres.json` checks counts, applied status, downloaded-byte equality and current build hashes.

This proves local simulator behavior only. No real provider credentials, charge, deployment or production activation is authorized. Temporary credentials remain in private scratch files outside source and evidence, deleted when the harness ends. Evidence contains fixture references, not credentials or provider secrets.
