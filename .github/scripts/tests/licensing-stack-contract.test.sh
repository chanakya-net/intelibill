#!/usr/bin/env bash
# Contract for the combined local stack (issue #835, P01-05).
#
# Resolves docker-compose.yml exactly as `docker compose --profile full up`
# would and asserts the service graph: one database preparation job, an
# explicit migrator and grants job per service database, each API waiting only
# for its own branch, loopback-only application ports and isolated runtime
# identities. Needs the Docker CLI (compose plugin) but no daemon.
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
scratch="$(mktemp -d)"
trap 'rm -rf "${scratch}"' EXIT

resolve() {
  # Resolve with no INTELIBILL_* overrides so the defaults are what is checked.
  env -u COMPOSE_PROFILES -u COMPOSE_PROJECT_NAME \
    $(env | sed -n 's/^\(INTELIBILL_[A-Z0-9_]*\)=.*/-u \1/p') \
    "$@"
}

(
  cd "${repository_root}"
  resolve docker compose --profile full config --format json >"${scratch}/full.json"
  resolve docker compose config --services >"${scratch}/db-only.txt"
  resolve env \
    INTELIBILL_CONTAINER_PREFIX=intelibill-p01-05-contract \
    INTELIBILL_POSTGRES_PORT=25432 \
    INTELIBILL_MERCHANT_API_PORT=25277 \
    INTELIBILL_WEB_PORT=24000 \
    INTELIBILL_LICENSING_API_PORT=25278 \
    INTELIBILL_ADMIN_WEB_PORT=24001 \
    INTELIBILL_MAILPIT_SMTP_PORT=21025 \
    INTELIBILL_MAILPIT_UI_PORT=28025 \
    docker compose -p intelibill-p01-05-contract --profile full config --format json >"${scratch}/isolated.json"
)

ruby - "${scratch}" <<'RUBY'
require "json"

scratch = ARGV.fetch(0)
full = JSON.parse(File.read(File.join(scratch, "full.json")))
isolated = JSON.parse(File.read(File.join(scratch, "isolated.json")))
db_only = File.read(File.join(scratch, "db-only.txt")).split

def assert(condition, message)
  raise "licensing stack contract: #{message}" unless condition
end

services = full.fetch("services")

expected = %w[
  postgres db-bootstrap mailpit
  merchant-migrate db-runtime-role backend frontend
  licensing-migrate licensing-runtime-role licensing-api admin-frontend
]
expected.each do |name|
  assert(services.key?(name), "full profile is missing service #{name}")
end
assert(db_only == ["postgres"], "database-only profile must start PostgreSQL alone, got #{db_only.inspect}")

def depends(services, name)
  services.fetch(name).fetch("depends_on", {}).transform_values { |v| v.fetch("condition") }
end

edges = {
  "db-bootstrap" => { "postgres" => "service_healthy" },
  "merchant-migrate" => { "db-bootstrap" => "service_completed_successfully" },
  "db-runtime-role" => { "merchant-migrate" => "service_completed_successfully" },
  "backend" => {
    "db-runtime-role" => "service_completed_successfully",
    "mailpit" => "service_started",
  },
  "frontend" => { "backend" => "service_healthy" },
  "licensing-migrate" => { "db-bootstrap" => "service_completed_successfully" },
  "licensing-runtime-role" => { "licensing-migrate" => "service_completed_successfully" },
  "licensing-api" => { "licensing-runtime-role" => "service_completed_successfully" },
  "admin-frontend" => { "licensing-api" => "service_healthy" },
}
edges.each do |service, required|
  actual = depends(services, service)
  required.each do |dependency, condition|
    assert(actual[dependency] == condition, "#{service} must wait for #{dependency} (#{condition}), got #{actual[dependency].inspect}")
  end
end

# Transitive closure: a licensing failure must never hold the merchant branch
# back, and the converse.
closure = lambda do |name, seen = []|
  depends(services, name).keys.each do |dep|
    next if seen.include?(dep)
    seen << dep
    closure.call(dep, seen)
  end
  seen
end
merchant_branch = %w[merchant-migrate db-runtime-role backend frontend]
licensing_branch = %w[licensing-migrate licensing-runtime-role licensing-api admin-frontend]
%w[backend frontend].each do |name|
  overlap = closure.call(name) & licensing_branch
  assert(overlap.empty?, "#{name} depends on the licensing branch: #{overlap.inspect}")
end
%w[licensing-api admin-frontend].each do |name|
  overlap = closure.call(name) & merchant_branch
  assert(overlap.empty?, "#{name} depends on the merchant branch: #{overlap.inspect}")
end

# Acyclic: every service resolves to PostgreSQL or Mailpit without revisiting itself.
services.each_key do |name|
  assert(!closure.call(name).include?(name), "#{name} has a circular dependency")
end

one_shots = %w[db-bootstrap merchant-migrate db-runtime-role licensing-migrate licensing-runtime-role]
one_shots.each do |name|
  restart = services.fetch(name).fetch("restart", "no")
  assert(restart == "no", "one-shot #{name} must not restart (#{restart})")
  assert(services.fetch(name).fetch("ports", []).empty?, "one-shot #{name} must not publish a port")
end

def env(services, name)
  services.fetch(name).fetch("environment", {}) || {}
end

def published(services, name)
  services.fetch(name).fetch("ports", []).map do |port|
    [port["host_ip"], port["published"].to_s, port["target"].to_s]
  end
end

{
  "backend" => ["5277", "8080"],
  "frontend" => ["4000", "4000"],
  "licensing-api" => ["5278", "8080"],
  "admin-frontend" => ["4001", "4000"],
}.each do |name, (host, target)|
  ports = published(services, name)
  assert(ports == [["127.0.0.1", host, target]], "#{name} must publish only 127.0.0.1:#{host}->#{target}, got #{ports.inspect}")
end
assert(published(services, "postgres") == [[nil, "5432", "5432"]], "PostgreSQL default port mapping changed")
published(services, "mailpit").each do |host_ip, _, _|
  assert(host_ip == "127.0.0.1", "Mailpit must stay on loopback")
end

builds = {
  "backend" => "src/backend/Dockerfile",
  "frontend" => "src/frontend/Dockerfile",
  "merchant-migrate" => "src/backend/Dockerfile.migrate",
  "licensing-api" => "src/licensing/Dockerfile",
  "licensing-migrate" => "src/licensing/Dockerfile.migrate",
  "admin-frontend" => "src/admin-frontend/Dockerfile",
}
builds.each do |name, dockerfile|
  build = services.fetch(name).fetch("build", {})
  assert(build["dockerfile"] == dockerfile, "#{name} must build #{dockerfile}, got #{build["dockerfile"].inspect}")
end

backend = env(services, "backend")
assert(backend["Database__Username"] == "intelibill_app", "merchant API must connect as its restricted runtime role")
assert(backend["Database__RequireRestrictedRuntimeRole"] == "true", "merchant runtime role guard disabled")
assert(backend["DistributedCache__CreateIfNotExists"].to_s != "true", "merchant API must not create its cache table")
assert(backend.keys.none? { |k| k.start_with?("Licensing") }, "merchant API received licensing settings")

licensing = env(services, "licensing-api")
assert(licensing["LicensingDatabase__Database"] == "intelibill_licensing_dev", "licensing API must use its own database")
assert(licensing["LicensingDatabase__Username"] == "intelibill_licensing_app", "licensing API must connect as its runtime role")
assert(licensing.keys.none? { |k| k.start_with?("LicensingMigrationDatabase__") }, "licensing API received migrator settings")
assert(licensing.keys.none? { |k| k.start_with?("Database__") }, "licensing API received merchant database settings")
assert(licensing["ASPNETCORE_ENVIRONMENT"] == "Development", "licensing API must state its local environment")

merchant_migrate = env(services, "merchant-migrate")
assert(merchant_migrate["Database__Database"] == "intelibill_dev", "merchant migrator targets the wrong database")
assert(merchant_migrate["Database__Username"] == "intelibill", "merchant migrator must remain the schema owner")

licensing_migrate = env(services, "licensing-migrate")
assert(licensing_migrate["LicensingMigrationDatabase__Database"] == "intelibill_licensing_dev", "licensing migrator targets the wrong database")
assert(licensing_migrate["LicensingMigrationDatabase__Username"] == "intelibill_licensing_migrator", "licensing migrator identity changed")
assert(licensing_migrate.keys.none? { |k| k.start_with?("Database__") }, "licensing migrator received merchant settings")

runtime_users = [backend["Database__Username"], licensing["LicensingDatabase__Username"]]
privileged = ["intelibill", "intelibill_licensing_migrator", "postgres"]
assert((runtime_users & privileged).empty?, "an API connects as a privileged identity")

admin = env(services, "admin-frontend")
assert(admin["API_ORIGIN"] == "http://licensing-api:8080", "admin web must forward only to the licensing API")
frontend = env(services, "frontend")
assert(frontend["API_ORIGIN"] == "http://backend:8080", "merchant web proxy target changed")

%w[db-bootstrap db-runtime-role licensing-runtime-role].each do |name|
  volumes = services.fetch(name).fetch("volumes", [])
  assert(volumes.any? { |v| v["target"] == "/scripts" && v["read_only"] }, "#{name} must mount scripts/db read-only")
end
bootstrap_args = Array(services.fetch("db-bootstrap")["entrypoint"]).join(" ")
assert(bootstrap_args.include?("local-licensing-role.sql"), "db-bootstrap must run local-licensing-role.sql")
assert(bootstrap_args.include?("stage=bootstrap"), "db-bootstrap must run the bootstrap stage")
grants_args = Array(services.fetch("licensing-runtime-role")["entrypoint"]).join(" ")
assert(grants_args.include?("stage=grants"), "licensing-runtime-role must run the grants stage")

# A second project with overrides shares nothing with the default stack.
iso = isolated.fetch("services")
iso.each do |name, service|
  container = service["container_name"].to_s
  assert(container.start_with?("intelibill-p01-05-contract"), "#{name} keeps a fixed container name: #{container.inspect}")
  Array(service["ports"]).each do |port|
    assert(port["published"].to_s.start_with?("2"), "#{name} keeps a default host port: #{port.inspect}")
  end
end
assert(
  env(iso, "backend")["App__BaseUrl"] == "http://localhost:24000",
  "merchant links must follow the web port override",
)
iso_volumes = isolated.fetch("volumes").values.map { |v| v["name"] }
assert(iso_volumes.all? { |v| v.start_with?("intelibill-p01-05-contract") }, "isolated project reuses a default volume: #{iso_volumes.inspect}")

puts "Licensing stack contract passed (#{services.size} services)."
RUBY
