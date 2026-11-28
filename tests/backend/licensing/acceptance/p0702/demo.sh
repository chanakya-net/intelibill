# Sourced by the guarded disposable full-stack runner.
identifiers=()
for field in account other_account payer manager shop_owner staff foreign_user shop membership affiliation term; do
  uuid="$(uuidgen | tr '[:upper:]' '[:lower:]')"
  identifiers+=(-v "$field=$uuid")
  [[ "$field" != account ]] || account="$uuid"
  [[ "$field" != manager ]] || manager="$uuid"
done
openssl rand -hex 20 >"$scratch/password"
hash="$(bun -e 'console.log(await Bun.password.hash((await Bun.stdin.text()).trim(), {algorithm:"bcrypt",cost:10}))' <"$scratch/password")"
sql intelibill_dev "${identifiers[@]}" -v "hash=$hash" <"$fixture_dir/merchant.sql"
sql intelibill_licensing_dev "${identifiers[@]}" <"$fixture_dir/licensing.sql"
compose exec -T -e DOTNET_ENVIRONMENT=Development -e LicensingMigrationDatabase__Host=postgres \
  -e LicensingMigrationDatabase__Database=intelibill_licensing_dev \
  -e LicensingMigrationDatabase__Username=intelibill_licensing_migrator \
  -e LicensingMigrationDatabase__Password=intelibill_licensing_migrator_password \
  licensing-api dotnet Intelibill.Licensing.Api.dll bootstrap-admin prepare --verified-email operator@p0702.test \
  --verification-reference p0702-local --admin-origin "$admin_web" >"$scratch/proof"
export P0702_ACCOUNT="$account" P0115_SCRATCH="$scratch" P0115_ADMIN_WEB="$admin_web" P0115_MAILPIT="$mailpit_ui"
tls_port="$(compose port backend 8443 | awk -F: '{print $NF}')"
export P0702_MERCHANT="https://localhost:$tls_port" P0702_LICENSING="$licensing_api" P0702_EVIDENCE_DIR="$evidence"
python3 "$fixture_dir/confirm-contact.py"
for ((i=0; i<40; i++)); do
  scheduled="$(sql intelibill_licensing_dev -c "SELECT count(*) FROM notification_email_deliveries WHERE job_id IS NOT NULL")"
  [[ "$scheduled" -eq 20 ]] && break
  sleep 1
done
if [[ "$scheduled" -ne 20 ]]; then
  sql intelibill_licensing_dev -c "SELECT scope_kind, audience, threshold, count(*) FROM effective_notification_jobs GROUP BY 1,2,3"
  compose logs --no-color --tail=500 licensing-api backend \
    | rg -o 'Notice planning[^"]+|Stage":"[A-Za-z]+|SqlState":"[A-Z0-9]+|StatusCode":[0-9]{3}' \
    | sort | uniq -c || true
  fail "Planner queued $scheduled deliveries before the first threshold"
fi
# Stall just the simulated manager's finish transaction after the real positive SMTP reply.
# Killing the worker at this point leaves its earlier Sending transaction committed.
sql intelibill_licensing_dev -v "manager=$manager" <<'SQL'
CREATE TABLE fixture_crash_recipient(actor uuid);
INSERT INTO fixture_crash_recipient VALUES (:'manager');
CREATE FUNCTION fixture_hold_finish() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER SET search_path TO pg_catalog AS $$
BEGIN
    IF NEW.state = 'SmtpAccepted' AND EXISTS (SELECT FROM public.notification_jobs j
        JOIN public.fixture_crash_recipient r ON r.actor = j.recipient_user_id WHERE j.id = NEW.job_id)
    THEN PERFORM pg_sleep(120); END IF;
    RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION fixture_hold_finish() FROM PUBLIC;
CREATE TRIGGER fixture_hold_finish BEFORE UPDATE ON notification_email_deliveries FOR EACH ROW EXECUTE FUNCTION fixture_hold_finish();
SQL
compose up -d --no-deps --force-recreate licensing-api >>"$evidence/restart.log" 2>&1
wait_for licensing-api healthy
for ((i=0; i<130; i++)); do
  held="$(sql intelibill_licensing_dev -c "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database()
    AND wait_event = 'PgSleep' AND usename = 'intelibill_licensing_app'")"
  [[ "$held" == 1 ]] && break
  sleep 1
done
[[ "$held" == 1 ]] || fail "The real SMTP acknowledgement did not reach the fixture crash boundary"
compose kill -s SIGKILL licensing-api >>"$evidence/restart.log" 2>&1
# Release this fixture's deliberately sleeping backend after its client process has died.
sql intelibill_licensing_dev -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = current_database()
  AND usename = 'intelibill_licensing_app' AND wait_event = 'PgSleep'" >/dev/null
sql intelibill_licensing_dev -c 'DROP TRIGGER fixture_hold_finish ON notification_email_deliveries; DROP FUNCTION fixture_hold_finish();'
compose up -d --no-deps licensing-api >>"$evidence/restart.log" 2>&1
wait_for licensing-api healthy
for ((i=0; i<75; i++)); do
  outcomes="$(sql intelibill_licensing_dev -c "
    SELECT count(*) FILTER (WHERE state='SmtpAccepted'), count(*) FILTER (WHERE state='Uncertain')
    FROM notification_email_deliveries WHERE job_id IS NOT NULL")"
  [[ "$outcomes" == "3|1" ]] && break
  sleep 1
done
[[ "$outcomes" == "3|1" ]] || fail "Expected three acknowledged and one uncertain reminder, observed $outcomes"
pass "Three SMTP acceptances and one uncertain reminder after process loss"
(
  cd src/frontend/licensing
  export NODE_PATH="$PWD/node_modules"
  bun x playwright test --config ../../../tests/backend/licensing/browser/playwright.p0702.config.ts
)
