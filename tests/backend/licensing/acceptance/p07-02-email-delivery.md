# Localized licensing email acceptance

From the checkout root, install the pinned Bun workspace dependencies and Playwright Chromium.
Use a local Docker daemon and a writable private evidence directory.

```bash
bun install --frozen-lockfile
P0702_EVIDENCE_DIR="$(mktemp -d)" bash tests/backend/licensing/acceptance/run-p0702-stack.sh
```

The runner builds the full stack under a fresh project name and loopback ports. It seeds a synthetic paid term,
verified merchant actors and exact shop affiliation, then approves and confirms a separate Bengali billing contact.
The planner and worker use the real databases and signed authority exchange.
The fixture stalls the manager's receipt transaction after SMTP acceptance and terminates the worker.
Chromium exercises operator enrollment, capability denial, delivery detail, keyboard navigation and the actual Mailpit search.

The evidence directory contains container/image and migration lists, cleanup output and a redacted browser observation.
Fixture keys and proof material stay in private temporary storage and are removed on exit.
The runner exercises local SMTP capture only.
