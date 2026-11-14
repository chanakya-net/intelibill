# Licensing rollout: local stack and CI (P01-05)

This covers what exists after issue #835: one Docker command that starts the
merchant and licensing systems side by side for local development, and CI that
validates both consumers of shared code. **Nothing here deploys licensing or
the admin web anywhere.** Production deployment, provider access and every
commercial flow stay behind their own later issues and gates.

## Start, restart, stop

```bash
docker compose --profile full up -d --build   # build and start everything
docker compose --profile full up -d           # start again; reruns migrations and grants
docker compose --profile full down            # stop; both databases are kept
```

`down -v` deletes the `postgres_data` volume and with it **both** databases. Use
it only when you mean to start from nothing.

`docker compose up -d` (no profile) still starts PostgreSQL alone for host-side
development; nothing about that workflow changed.

## What starts, in order

| Step | Service | Runs as | Exits / serves |
| --- | --- | --- | --- |
| 1 | `postgres` | — | PostgreSQL 17, volume `postgres_data` |
| 2 | `db-bootstrap` | `intelibill` (superuser) | creates the licensing roles and database once, re-states role attributes, confines `CONNECT` on both databases; exit 0 |
| 3a | `merchant-migrate` | `intelibill` (merchant schema owner) | merchant EF migration bundle; exit 0 |
| 4a | `db-runtime-role` | `intelibill` | merchant runtime grants (`scripts/db/local-runtime-role.sql`); exit 0 |
| 5a | `backend` | `intelibill_app` | merchant API, healthy on `/health/live` |
| 6a | `frontend` | — | merchant web |
| 3b | `licensing-migrate` | `intelibill_licensing_migrator` (licensing database owner) | licensing EF migration bundle; exit 0 |
| 4b | `licensing-runtime-role` | `intelibill` | licensing runtime grants, then refuses to exit 0 if the runtime login could change schema, own anything or assume a role |
| 5b | `licensing-api` | `intelibill_licensing_app` | licensing API, healthy on `/health/ready` |
| 6b | `admin-frontend` | — | admin web |
| — | `mailpit` | — | local mail capture, started before `backend` |

Each step waits for the previous one to **exit 0** (`service_completed_successfully`)
or be healthy. The `a` and `b` branches share only steps 1–2, so a failing
migration can only ever block its own API and web. `docker compose up` reports
such a failure and stops starting anything else it had not started yet; start
the healthy branch alone with `docker compose --profile full up -d frontend`
(merchant) or `... up -d admin-frontend` (licensing). A branch that is already
running keeps serving when a later `up` fails in the other one. One-shot jobs never restart on their own; every `up` runs them again,
which is how a newly added migration is applied and granted. An API restart never
reruns a migration, and neither API issues DDL at startup.

The licensing EF model is empty until later issues add migrations. Its bundle
still runs, connects as the migrator and exits 0 with nothing to apply.

## Ports (loopback only)

| Service | Default | Override |
| --- | --- | --- |
| Merchant API | `127.0.0.1:5277` | `INTELIBILL_MERCHANT_API_PORT` |
| Merchant web | `127.0.0.1:4000` | `INTELIBILL_WEB_PORT` (also moves `App__BaseUrl`) |
| Licensing API | `127.0.0.1:5278` | `INTELIBILL_LICENSING_API_PORT` |
| Admin web | `127.0.0.1:4001` | `INTELIBILL_ADMIN_WEB_PORT` |
| Mailpit UI / SMTP | `127.0.0.1:8025` / `127.0.0.1:1025` | `INTELIBILL_MAILPIT_UI_PORT` / `INTELIBILL_MAILPIT_SMTP_PORT` |
| PostgreSQL | `5432` | `INTELIBILL_POSTGRES_PORT` |

`INTELIBILL_CONTAINER_PREFIX` (default `intelibill`) prefixes every container
name. Together with `docker compose -p <name>` (own volume and network) these
let a second, fully isolated stack run beside your normal one, which is what the
acceptance harness does.

## Databases and identities (local only)

| Database | Owner / migrator | Runtime login |
| --- | --- | --- |
| `intelibill_dev` | `intelibill` (unchanged) | `intelibill_app` |
| `intelibill_licensing_dev` | `intelibill_licensing_migrator` | `intelibill_licensing_app` |

The passwords are fixed, public development values written in
`docker-compose.yml` and `scripts/db/local-licensing-role.sql`. They exist only
in this local stack. Both runtime logins are `NOSUPERUSER NOBYPASSRLS
NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT`, belong to no role, own
nothing, hold schema `USAGE` without `CREATE`, cannot create temporary tables
and can connect only to their own database (PUBLIC `CONNECT`/`TEMPORARY` is
revoked on both). Data grants cover existing tables and, through default
privileges `FOR ROLE` the actual migrator, tables it creates later.

`scripts/db/local-licensing-role.sql` refuses to run unless given
`-v environment=Development` (or `Testing`), is serialized by an advisory lock
and never drops, recreates or reassigns anything: an unexpected owner or role
membership stops it with an error instead.

### Existing developer volumes

The first full `up` after pulling this change, on a volume that already holds
merchant data:

- `merchant-migrate` applies any merchant migrations you had not yet applied
  (previously you ran `dotnet ef database update` by hand);
- `db-bootstrap` revokes PUBLIC `CONNECT` and `TEMPORARY` on `intelibill_dev`.
  `intelibill_app` keeps its explicit `CONNECT`; the superuser `intelibill`
  is unaffected. Any other login you created yourself needs its own
  `GRANT CONNECT`;
- the licensing database and roles are created beside it. Merchant data,
  ownership and migration history are left as they were.

## Configuration keys

| Service | Prefix / keys | Notes |
| --- | --- | --- |
| `merchant-migrate` | `Database__Host/Port/Database/Username/Password` | schema owner |
| `backend` | as before, plus `DistributedCache__CreateIfNotExists=false` | unchanged otherwise; still `ASPNETCORE_ENVIRONMENT=Development` for Mailpit and relaxed rate limits |
| `licensing-migrate` | `LicensingMigrationDatabase__Host/Port/Database/Username/Password` | migrator only; never given to the API |
| `licensing-api` | `ASPNETCORE_ENVIRONMENT=Development`, `LicensingDatabase__Host/Port/Database/Username/Password` | image default is Production |
| `frontend` | `API_ORIGIN=http://backend:8080` | forwards `/api` and `/hubs` |
| `admin-frontend` | `API_ORIGIN=http://licensing-api:8080`, `PORT` | forwards only `/api/admin/**`; every other `/api` or `/hubs` path answers 404 `no-store` |

## What you can do locally today

- Enrol a merchant through the merchant web with any address (for example
  `you@example.test`): the verification email lands in Mailpit at
  `http://127.0.0.1:8025`, and nothing leaves the machine.
- Open the admin web at `http://127.0.0.1:4001`: the Provider Console shell
  loads, reports **No provider session** and **No provider permissions**, and
  a merchant session in the same browser confers nothing there.
- Probe `GET http://127.0.0.1:5278/health/live` and `/health/ready`.

Not available yet, by design (each belongs to a later issue): provider
identity bootstrap, invitations, sign-in, MFA and sessions for the admin web
(`/api/admin/**` currently answers 404 from the licensing API); licensing email
(Mailpit is reachable, but the licensing API has no mail sender yet); and every
commercial flow — catalogue, simulator, payments, seeded plans or accounts.
There are no default admin credentials and no production seed. No real charge
or real email is ever made by this stack.

## Troubleshooting

- `docker compose ps -a` shows each one-shot's exit code. A nonzero
  `merchant-migrate` or `licensing-migrate` leaves its own API and web
  unstarted (`Created`); read it with `docker compose logs <service>` and start
  the other branch alone as described above.
- `licensing-runtime-role` exits nonzero when the runtime login has gained an
  attribute, membership, ownership or schema privilege it must not have. Fix the
  role (as the superuser) rather than editing the check.
- `db-bootstrap` exits nonzero if `intelibill_licensing_dev` exists with another
  owner or a service login belongs to a role.
- A port already in use: stop the other process or set the matching
  `INTELIBILL_*_PORT`.

## CI

`Application Release` (`.github/workflows/deploy.yml`) validates pull requests
that touch merchant, licensing, admin, shared, workspace, Compose or local
database script paths. Its single validation job builds the root
`Intelibill.slnx` (both backends and the shared projects), runs every test
project under `tests/backend`, `tests/licensing` and `tests/shared` once,
installs the root Bun workspace once, builds and tests the merchant and admin
web apps, checks the Compose contract and the admin runtime server, and builds
and scans all six images. Licensing and admin images are built for validation
only; the publish and dev/prod release jobs, and the main-push trigger set that
starts them, are unchanged.

## Acceptance harness

`scripts/dev/test-licensing-stack.sh` runs the real workflow under isolated
Compose projects (never your default stack, containers or volume) and records
evidence. See `tests/licensing/acceptance/p01-05-infrastructure.md`.

```bash
P0105_PROJECT=intelibill-p01-05-local P0105_ARTIFACTS=/tmp/p0105 \
  bash scripts/dev/test-licensing-stack.sh --case all
bash scripts/dev/test-licensing-stack.sh --case cleanup   # same P0105_PROJECT
```
