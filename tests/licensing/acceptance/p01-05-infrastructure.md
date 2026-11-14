# P01-05 acceptance: combined local stack and dual-consumer CI

Issue #835. Acceptance criteria:

1. Fresh and restarted full stacks become usable with isolated runtime users and
   no API-startup DDL.
2. Shared-source edits trigger both consumers in CI; no production seed,
   credential, real charge or real email is needed.

Every scenario below is executable. Nothing in it contacts an external email,
payment or identity provider; all mail goes to the stack's own Mailpit and all
identities are fixed local development values.

## Executable checks

| Check | Command (repository root) |
| --- | --- |
| Compose graph contract | `bash .github/scripts/tests/licensing-stack-contract.test.sh` |
| Workflow contract | `bash .github/scripts/tests/validate-phase-11-workflows.test.sh` |
| Web image contract | `bash .github/scripts/tests/frontend-image-contract.test.sh` |
| Migration image contract (builds both bundles) | `bash .github/scripts/tests/migration-image-contract.test.sh` |
| Admin runtime server | `node --test src/admin-frontend/tests/server.test.mjs` |
| Production excludes local settings | `tofu -chdir=.tofu/modules/environment-infrastructure test -filter=tests/contract.tftest.hcl` (after `init -backend=false`) |
| Real stack, PostgreSQL and Chromium | `P0105_PROJECT=intelibill-p01-05-<name> P0105_ARTIFACTS=<dir> bash scripts/dev/test-licensing-stack.sh --case all` |

The harness only acts on Compose projects named `intelibill-p01-05-*` whose
containers and volumes carry that project's label, on its own loopback ports
(14xxx main, 24xxx/34xxx failure branches, 44xxx reused volume). It never stops,
mounts or deletes the default `intelibill` stack or its `postgres_data` volume.

## Scenarios

### S1 Fresh full stack (criterion 1) — harness `--case fresh`

Given an empty volume, `docker compose --profile full up -d --build` runs
`db-bootstrap`, `merchant-migrate`, `db-runtime-role`, `licensing-migrate` and
`licensing-runtime-role` to exit 0; then:

- merchant API `/health/live` and `/health/ready` 200, licensing API
  `/health/live` and `/health/ready` 200 (`Cache-Control: no-store`), merchant
  web and admin web 200, Mailpit API 200;
- the admin origin answers `/api/shops/me` with 404 (never forwarded).

### S2 Isolated runtime identities (criterion 1) — `--case fresh`

Against the real PostgreSQL catalog and real connection attempts:

- `intelibill_app` and `intelibill_licensing_app` are NOSUPERUSER, NOBYPASSRLS,
  NOCREATEDB, NOCREATEROLE, NOREPLICATION, NOINHERIT and members of no role;
- each runtime login is refused `CONNECT` to the other service's database;
- each is refused `CREATE TABLE`, `CREATE TEMP TABLE` and `SET ROLE` to its
  database owner; the merchant one is refused `ALTER TABLE shops`;
- merchant tables keep row-level security; under the browser scenario the
  runtime role reads a shop's customer only with that shop's
  `app.active_shop_id` (unscoped 0, own shop 1, other shop 0);
- tables the migrator creates after the grants job are usable by the runtime
  login (default privileges `FOR ROLE` the actual migrator).

### S3 No API-startup DDL (criterion 1) — `--case fresh`

With `log_statement = ddl` enabled in the fixture PostgreSQL only, a migrator
DDL control statement is captured, then both APIs are restarted concurrently
and probed repeatedly: zero statements are logged for either runtime login.

### S4 Restart and retention (criterion 1) — `--case fresh`

A repeated `up` reruns every one-shot (new `StartedAt`, exit 0) without changing
data or migration history; `down` (no `-v`) then `up` keeps merchant migration
history, both sentinels and the licensing database owner. Stopping one API/web
pair leaves the other serving.

### S5 Existing merchant-only volume (criterion 1) — `--case reused`

Given a volume prepared the pre-#835 way (PostgreSQL alone, merchant bundle,
old runtime role, merchant data):

- the preparation script refuses `environment=Production` before any DDL;
- two concurrent `db-bootstrap` runs both exit 0;
- the full `up` keeps the merchant data, owner, migration count and runtime
  access, and adds the licensing database beside it.

### S6 Failing migration blocks only its own branch (criterion 1) — `--case failure`

With a migrator forced to exit 7 on a fresh isolated project: `up` fails
visibly; that branch's grants job, API and web never start; the other branch
then starts alone and serves; a repeated failing `up` leaves it serving. Run for
both `licensing-migrate` and `merchant-migrate`.

### S7 Browser on the real images (criterion 1) — `--case browser`

Chromium, no route interception or mocks: enrol a `.test` address on the
merchant web, open the verification link captured by the stack's Mailpit,
finish sign-up, create a shop and a customer through the real API; the
database holds exactly that user, shop and membership. After `down`/`up`, the
same person signs in through the UI and still owns exactly that shop. On the
admin origin in the same browser: Provider Console, "No provider session",
"No provider permissions", the shared confirmation dialog, no API/hub/worker
requests, empty storage, no service worker, and `/api/shops/me` → 404
`no-store`.

### S8 Both consumers in CI (criterion 2) — workflow contract

Pull requests touching any merchant, licensing, admin, shared, root workspace,
Compose or local database script path run one validation job that builds the
root `Intelibill.slnx`, runs each test project under `tests/backend`,
`tests/licensing` and `tests/shared` once, installs the root Bun workspace once,
builds and tests both web apps once each, checks the Compose contract and admin
server, and builds and scans all six images. Main-push triggers equal the
pre-#835 release set exactly; no job publishes or deploys licensing/admin
images; dev/prod gates are unchanged.

### S9 No production fixture leakage (criterion 2) — OpenTofu contract

`prod_excludes_local_stack_settings` plans the production module and asserts
no API, web or migration container carries Development, Mailpit capture,
relaxed rate limits, literal JWT/outbox keys, database passwords, licensing
database settings or any local fixture identity/database/origin.

## Recorded results (2026-11-14, issue worktree, Docker Desktop 2 CPU / 12.6 GB)

Red before implementation (assertions added first, product files absent):

| Check | Exit | Failing assertion |
| --- | --- | --- |
| `licensing-stack-contract.test.sh` | 1 | `full profile is missing service db-bootstrap` |
| `validate-phase-11-workflows.test.sh` | 1 | `pull-request validation does not watch src/licensing/**` |
| `frontend-image-contract.test.sh` | 1 | `Admin web Dockerfile is missing` |
| `migration-image-contract.test.sh` | 1 | `licensing migration Dockerfile is missing` (merchant static checks passed first) |
| `node --test src/admin-frontend/tests/server.test.mjs` | 1 | `ERR_MODULE_NOT_FOUND .../src/admin-frontend/server.mjs` |

The harness was not run against the baseline `docker-compose.yml`: its fixed
container names (`intelibill_postgres`, ...) collide with the developer's
existing containers, which this work must not touch.

Green after implementation (`P0105_PROJECT=intelibill-p01-05-835`):

- S1: all five one-shots exit 0; probes 200 on 14277/14278/14000/14001/14825;
  admin `/api/shops/me` 404.
- S2: attributes `f|f|f|f|f|f` for both runtime roles; cross-database
  `FATAL: permission denied for database ... User does not have CONNECT
  privilege` both ways; `permission denied for schema public`; `permission
  denied to create temporary tables`; `permission denied to set role`; `must
  be owner of table shops`; RLS `owner=1 runtime_unscoped=0 runtime_own_shop=1
  runtime_other_shop=0`.
- S3: control captured 1; runtime DDL across concurrent restarts and 10
  readiness probes = 0.
- S4: `merchant_history=72 licensing_history=present merchant_sentinel=1
  licensing_sentinel=2 licensing_owner=intelibill_licensing_migrator`
  unchanged across repeated `up` and `down`/`up`.
- S5: Production refused with no roles created; concurrent bootstraps exit 0;
  72 merchant migrations, sentinel, owner and runtime access kept.
- S6: `licensing-migrate` and `merchant-migrate` each exit 7, `up` fails, own
  grants/API/web stay `Created`; the sibling starts alone and keeps serving.
- S7: 2 Playwright tests pass in each phase; rows
  `users=1 shops=1 memberships=1` identical after `down`/`up`. Screenshots and
  traces under `P0105_ARTIFACTS` (credentials are kept in a separate
  `.fixture-credentials.json`, never printed).
- S8/S9: workflow contract passes; OpenTofu module 33/33, dev 3/3, prod 3/3.

## Review regressions

| Command | Before review fixes | Expected result |
| --- | --- | --- |
| `node --test src/admin-frontend/tests/server.test.mjs` | Exit 1. A raw mixed-separator path produced an upstream request. | Both mixed-separator requests return 400 or 404 with `Cache-Control: no-store` and zero upstream requests. The admin prefix and its descendants keep forwarding. |
| `bash .github/scripts/tests/licensing-stack-harness.test.sh ambient` | Exit 1. A stubbed Docker up resolved a foreign volume. | Fresh startup and cleanup ignore ambient Compose files and `.env`. |
| `bash .github/scripts/tests/licensing-stack-harness.test.sh foreign` | | The resolved model and ownership checks refuse a foreign volume before up or cleanup. |
| `bash .github/scripts/tests/licensing-stack-harness.test.sh build` | Exit 1. The simulated Compose exit 17 became stack-up exit 0. | Stack-up returns 17, records the Compose log path and skips the health wait. |

Run the full acceptance command above on a new project to exercise the
fresh-volume, reused-volume, failed-migrator and Chromium cases with these
guards. Use `--case cleanup` with the same project name. Compare container IDs,
network ID and volume metadata for the developer project before and after.

Known limits: the licensing EF model is empty, so its bundle applies nothing
(it still connects and exits 0). `/api/admin/**` forwarding is proven against a
controlled upstream in the server tests only; the licensing API has no admin
routes yet, so no admin authentication is claimed. No live email, payment or
provider was used.
