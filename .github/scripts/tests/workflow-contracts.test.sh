#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"

ruby - "${repository_root}" <<'RUBY'
require "yaml"

root = ARGV.fetch(0)
workflow_dir = File.join(root, ".github", "workflows")

def assert(condition, message)
  raise message unless condition
end

def load_workflow(workflow_dir, name)
  path = File.join(workflow_dir, name)
  document = YAML.load_file(path)
  assert(document.is_a?(Hash), "#{name}: workflow must be a mapping")
  document
end

def triggers(workflow)
  value = workflow["on"]
  assert(value.is_a?(Hash), "workflow trigger must be an explicit mapping")
  value
end

def commands(job)
  rendered = []
  job_env = job.fetch("env", {})
  rendered.concat(job_env.values.map(&:to_s)) if job_env.is_a?(Hash)
  Array(job["steps"]).each do |step|
    next unless step.is_a?(Hash)

    rendered << step.fetch("run", "").to_s
    step_env = step.fetch("env", {})
    rendered.concat(step_env.values.map(&:to_s)) if step_env.is_a?(Hash)
    with_values = step.fetch("with", {})
    rendered.concat(with_values.values.map(&:to_s)) if with_values.is_a?(Hash)
  end
  rendered.join("\n")
end

plan = load_workflow(workflow_dir, "infra-plan.yml")
apply = load_workflow(workflow_dir, "infra-apply.yml")
deploy = load_workflow(workflow_dir, "deploy.yml")
oidc_smoke = load_workflow(workflow_dir, "oidc-smoke-test.yml")

%w[
  backend-pr-ci.yml
  backend-main-ci.yml
  frontend-pr-ci.yml
  frontend-main-ci.yml
].each do |duplicate_workflow|
  assert(
    !File.exist?(File.join(workflow_dir, duplicate_workflow)),
    "#{duplicate_workflow} duplicates application validation from deploy.yml",
  )
end

Dir.glob(File.join(workflow_dir, "*.{yml,yaml}")).sort.each do |path|
  workflow = load_workflow(workflow_dir, File.basename(path))
  run_name = workflow.fetch("run-name", "").to_s
  assert(run_name.include?("github.workflow"), "#{File.basename(path)}: run name omits workflow")
  assert(
    run_name.include?("github.event.pull_request.number") &&
      run_name.include?("github.ref_name"),
    "#{File.basename(path)}: run name omits event context",
  )
end

[plan, apply, deploy].each do |workflow|
  workflow.fetch("jobs").each do |job_name, job|
    checkout_steps = Array(job["steps"]).select do |step|
      step.is_a?(Hash) && step.fetch("uses", "").start_with?("actions/checkout@")
    end
    checkout_steps.each do |step|
      if workflow.equal?(deploy) && job_name == "badges"
        assert(
          step.dig("with", "persist-credentials") == true,
          "badge publication must explicitly opt into push credentials",
        )
        next
      end

      assert(
        step.dig("with", "persist-credentials") == false,
        "checkout must not persist a workflow token into the build context",
      )
    end
  end
end

deploy_confinement_steps = oidc_smoke
  .fetch("jobs")
  .fetch("deploy_is_confined")
  .fetch("steps")
[
  "Cannot read state",
  "Cannot create resources",
  "Cannot modify the database server",
].each do |step_name|
  denial_step = deploy_confinement_steps.find do |step|
    step.fetch("name", "") == step_name
  end
  assert(!denial_step.nil?, "OIDC smoke test is missing '#{step_name}'")
  assert(
    denial_step.fetch("run", "").include?("required permissions"),
    "#{step_name} does not recognize Azure CLI's generic RBAC denial",
  )
end

plan_triggers = triggers(plan)
assert(plan_triggers.key?("pull_request"), "plan must run for pull requests")
assert(!plan_triggers.key?("pull_request_target"), "plan must reject pull_request_target")
plan_paths = plan_triggers.dig("pull_request", "paths")
assert(
  plan_paths.include?(".tofu/envs/**") &&
    plan_paths.include?(".tofu/modules/**") &&
    plan_paths.include?(".tofu/scripts/**"),
  "plan paths must cover automatically managed OpenTofu",
)
assert(
  !plan_paths.include?(".tofu/**"),
  "manual bootstrap changes must not imply an automatic plan",
)
assert(
  plan["permissions"] == {
    "contents" => "read",
    "id-token" => "write",
    "pull-requests" => "write",
  },
  "plan permissions changed",
)
plan_job = plan.fetch("jobs").fetch("plan")
assert(!plan_job.key?("environment"), "plan must not enter a GitHub environment")
assert(
  plan_job.dig("strategy", "matrix", "layer") == %w[shared dev prod],
  "plan matrix must cover shared, dev, and prod",
)
plan_commands = commands(plan_job)
assert(plan_commands.include?("vars.AZURE_CLIENT_ID_PLAN"), "plan identity missing")
assert(!plan_commands.include?("vars.AZURE_CLIENT_ID_INFRA"), "plan uses infra identity")
assert(!plan_commands.include?("vars.AZURE_CLIENT_ID_DEPLOY"), "plan uses deploy identity")
assert(
  plan_commands.include?("-lock=false"),
  "read-only pull-request plans must not acquire the state lock",
)
assert(
  plan_commands.include?("-refresh=false"),
  "read-only pull-request plans must not refresh privileged resource data",
)
assert(
  plan_job.fetch("if").include?(
    "github.event.pull_request.head.repo.full_name == github.repository",
  ),
  "plan must reject fork pull requests before login",
)
enforce_plan_step = plan_job.fetch("steps").find do |step|
  step.fetch("name", "") == "Enforce successful plan"
end
assert(!enforce_plan_step.nil?, "plan result enforcement missing")
assert(
  enforce_plan_step.fetch("run").include?("0|2)"),
  "plan result enforcement must accept only exit codes 0 and 2",
)

apply_jobs = apply.fetch("jobs")
apply_paths = triggers(apply).dig("push", "paths")
assert(
  apply_paths.include?(".tofu/envs/**") &&
    apply_paths.include?(".tofu/modules/**") &&
    apply_paths.include?(".tofu/scripts/**"),
  "apply paths must cover automatically managed OpenTofu",
)
assert(
  !apply_paths.include?(".tofu/**"),
  "manual bootstrap changes must not imply an automatic apply",
)
assert(apply_jobs.dig("dev", "needs") == "shared", "dev apply must follow shared")
assert(apply_jobs.dig("prod", "needs") == "dev", "prod apply must follow dev")
%w[shared dev prod].each do |environment|
  assert(
    apply_jobs.dig(environment, "environment") == environment,
    "#{environment} apply must enter its environment",
  )
end
assert(apply.dig("concurrency", "group") == "infra-apply-main", "apply concurrency missing")
apply_commands = apply_jobs.values.map { |job| commands(job) }.join("\n")
assert(apply_commands.include?("vars.AZURE_CLIENT_ID_INFRA"), "infra identity missing")
assert(!apply_commands.include?("vars.AZURE_CLIENT_ID_PLAN"), "apply uses plan identity")
assert(!apply_commands.include?("vars.AZURE_CLIENT_ID_DEPLOY"), "apply uses deploy identity")
assert(
  commands(apply_jobs.fetch("shared")).include?("guard-shared-egress-plan.sh"),
  "shared apply must guard the saved plan",
)
assert(
  commands(apply_jobs.fetch("shared")).include?("check-container-app-egress.sh"),
  "shared apply must verify the live firewall inventory",
)

deploy_jobs = deploy.fetch("jobs")
deploy_triggers = triggers(deploy)
assert(
  deploy_triggers.key?("pull_request"),
  "release workflow must validate changed images before merge",
)
release_images = {
  "build_api" => {
    "dockerfile" => "src/backend/intelibill/Dockerfile",
    "repository" => "${{ env.API_IMAGE }}",
    "consumer" => "needs.build_api.outputs.image",
  },
  "build_web" => {
    "dockerfile" => "src/frontend/intelibill/Dockerfile",
    "repository" => "${{ env.WEB_IMAGE }}",
    "consumer" => "needs.build_web.outputs.image",
  },
  "build_migrate" => {
    "dockerfile" => "src/backend/intelibill/Dockerfile.migrate",
    "repository" => "${{ env.MIGRATE_IMAGE }}",
    "consumer" => "needs.build_migrate.outputs.image",
  },
}
release_images.each_key do |job_name|
  assert(
    deploy_jobs.key?(job_name),
    "#{job_name} must be an independent release-image job",
  )
end
release_images.each do |job_name, image|
  release_build = deploy_jobs.fetch(job_name)
  assert(
    release_build["needs"] == "validate",
    "#{job_name} must depend only on validation so image builds can run in parallel",
  )
  # validate follows jobs that are skipped for unchanged projects. Without an
  # explicit status check a skipped ancestor would skip the release as well.
  assert(
    release_build.fetch("if").include?("!cancelled()") &&
      release_build.fetch("if").include?("needs.validate.result == 'success'"),
    "#{job_name} must run only after a successful validation gate",
  )
  assert(
    release_build.fetch("if").include?("github.event_name != 'pull_request'"),
    "#{job_name} must reject pull-request package writes",
  )
  assert(
    release_build.dig("permissions", "packages") == "write",
    "#{job_name} requires package write permission",
  )
  assert(
    release_build.dig("outputs", "image").to_s.include?("steps.reference.outputs.image"),
    "#{job_name} immutable image output missing",
  )
  build_step = release_build.fetch("steps").find do |step|
    step.fetch("uses", "").start_with?("docker/build-push-action@")
  end
  assert(!build_step.nil?, "#{job_name} image build missing")
  assert(
    build_step.dig("with", "file") == image.fetch("dockerfile"),
    "#{job_name} does not build #{image.fetch("dockerfile")}",
  )
  assert(
    build_step.dig("with", "push") == true,
    "#{job_name} does not publish its image",
  )
  assert(
    build_step.dig("with", "tags").to_s.include?(image.fetch("repository")),
    "#{job_name} does not publish to #{image.fetch("repository")}",
  )
  login_step = release_build.fetch("steps").find do |step|
    step.fetch("name", "") == "Login to GHCR"
  end
  assert(!login_step.nil?, "#{job_name} GHCR login step missing")
  scan_step = release_build.fetch("steps").find do |step|
    step.fetch("uses", "").start_with?("aquasecurity/trivy-action@")
  end
  assert(!scan_step.nil?, "#{job_name} image scan missing")
  assert(
    scan_step.dig("with", "image-ref").to_s.include?("steps.reference.outputs.image"),
    "#{job_name} does not scan its immutable image",
  )
end
# Validation is scoped: each project is built and tested by its own job, and
# only when a change can affect it. detect-changed-projects.sh owns the mapping
# from changed paths to projects (its own test covers the mapping itself).
validation_job_names = %w[changes contracts backend merchant_web admin_web images]
validation_job_names.each do |job_name|
  assert(deploy_jobs.key?(job_name), "#{job_name} validation job missing")
end
validation_job = deploy_jobs.fetch("validate")
assert(
  validation_job["needs"] == validation_job_names,
  "the validation gate must follow every validation job",
)
assert(
  validation_job.fetch("if").include?("always()"),
  "the validation gate must report even when project jobs are skipped or fail",
)
gate_commands = commands(validation_job)
%w[changes contracts].each do |job_name|
  assert(
    gate_commands.include?("needs.#{job_name}.result"),
    "the validation gate ignores #{job_name}",
  )
end
%w[backend merchant_web admin_web images].each do |job_name|
  assert(
    gate_commands.include?("needs.#{job_name}.result"),
    "the validation gate ignores #{job_name}",
  )
  assert(
    deploy_jobs.fetch(job_name)["needs"] == "changes",
    "#{job_name} must be selected by change detection",
  )
  assert(
    deploy_jobs.fetch(job_name).fetch("if").include?("needs.changes.outputs."),
    "#{job_name} must run only for the projects a change affects",
  )
end

changes_job = deploy_jobs.fetch("changes")
changes_commands = commands(changes_job)
assert(
  changes_commands.include?(".github/scripts/detect-changed-projects.sh"),
  "change detection must use the tested detection script",
)
assert(
  changes_commands.include?("--all"),
  "change detection must validate everything when no base commit is comparable",
)
%w[backend_suites images merchant_web admin_web].each do |output|
  assert(
    changes_job.dig("outputs", output).to_s.include?("steps.detect.outputs.#{output}"),
    "change detection does not publish #{output}",
  )
end
detect_script = File.read(File.join(root, ".github", "scripts", "detect-changed-projects.sh"))

backend_job = deploy_jobs.fetch("backend")
backend_commands = commands(backend_job)
assert(
  backend_job.dig("strategy", "matrix", "suite").to_s.include?("needs.changes.outputs.backend_suites"),
  "backend suites must come from change detection",
)
assert(
  backend_commands.scan("dotnet test").length == 1,
  "backend tests must have exactly one owner in application validation",
)
all_validation_commands = validation_job_names.map { |name| commands(deploy_jobs.fetch(name)) }.join("\n")
assert(
  all_validation_commands.scan("dotnet test").length == 1,
  "backend tests must have exactly one owner in application validation",
)
assert(
  backend_commands.include?("matrix.suite.roots"),
  "a backend suite must test only its own test roots",
)

# P01-05: both consumers of shared code are validated together. A change to
# src/backend/shared (or the root workspace) must build and test the merchant
# and licensing backends, and a shared web change both web apps, each exactly
# once.
pre_835_release_paths = %w[
  .github/scripts/**
  .github/workflows/deploy.yml
  src/backend/intelibill/**
  src/backend/shared/**
  src/frontend/intelibill/**
  src/frontend/shared/**
  tests/backend/intelibill/**
  Directory.Build.props
  Directory.Packages.props
  global.json
]
validation_only_paths = %w[
  src/backend/licensing/**
  src/frontend/licensing/**
  tests/backend/licensing/**
  tests/backend/shared/**
  tests/contracts/licensing/**
  Intelibill.slnx
  package.json
  bun.lock
  docker-compose.yml
  scripts/db/**
  scripts/dev/**
  .dockerignore
]
documentation_exclusion = "!**/*.md"
pull_request_paths = deploy_triggers.dig("pull_request", "paths")
(pre_835_release_paths + validation_only_paths).each do |path|
  assert(
    pull_request_paths.include?(path),
    "pull-request validation does not watch #{path}",
  )
  # Every watched path must select something, or a run would start and
  # validate nothing.
  watched = path.sub(%r{/?\*\*\z}, "")
  assert(
    detect_script.include?(watched),
    "change detection does not classify watched path #{path}",
  )
end
assert(
  pull_request_paths.last == documentation_exclusion,
  "documentation alone must not start pull-request validation",
)
assert(
  deploy_triggers.dig("push", "paths") == pre_835_release_paths + [documentation_exclusion],
  "main pushes must keep the pre-P01-05 release trigger set; licensing-only changes validate on pull requests without publishing merchant images",
)
assert(
  backend_commands.include?("dotnet restore Intelibill.slnx") &&
    backend_commands.include?("dotnet build Intelibill.slnx"),
  "validation must restore and build the root solution so both backends compile against shared code",
)
%w[tests/backend/intelibill tests/backend/licensing tests/backend/shared].each do |test_root|
  assert(
    detect_script.include?(test_root),
    "backend test enumeration omits #{test_root}",
  )
end

{
  "merchant_web" => ["src/frontend/intelibill", "merchant web"],
  "admin_web" => ["src/frontend/licensing", "admin web"],
}.each do |job_name, (directory, consumer)|
  web_steps = Array(deploy_jobs.fetch(job_name)["steps"]).select { |step| step.is_a?(Hash) }
  root_install = web_steps.select do |step|
    step.fetch("run", "").include?("bun install --frozen-lockfile") && !step.key?("working-directory")
  end
  assert(root_install.length == 1, "#{consumer} dependencies must be installed once from the root workspace")
  assert(
    web_steps.none? do |step|
      step.key?("working-directory") && step.fetch("run", "").include?("bun install")
    end,
    "app-local bun install bypasses the root workspace lockfile",
  )
  owned = web_steps.select { |step| step["working-directory"] == directory }
  owned_runs = owned.map { |step| step.fetch("run", "") }.join("\n")
  assert(owned_runs.scan("bun run build").length == 1, "#{consumer} must be built exactly once")
  assert(owned_runs.scan("bun run test").length == 1, "#{consumer} tests must have exactly one owner")
  assert(
    web_steps.all? { |step| !step.key?("working-directory") || step["working-directory"] == directory },
    "#{job_name} must build and test only the #{consumer}",
  )
end
assert(
  all_validation_commands.scan("bun run test").length == 2,
  "only the two web consumers may own frontend test runs",
)
contract_commands = commands(deploy_jobs.fetch("contracts"))
assert(
  !deploy_jobs.fetch("contracts").key?("needs") && !deploy_jobs.fetch("contracts").key?("if"),
  "delivery contracts must be checked on every run",
)
assert(
  contract_commands.include?("detect-changed-projects.test.sh"),
  "validation must test the change-detection mapping it relies on",
)
assert(
  contract_commands.include?("licensing-stack-contract.test.sh"),
  "validation must check the combined local stack contract",
)
assert(
  contract_commands.include?("licensing-stack-harness.test.sh"),
  "validation must check fixture isolation and Compose failure propagation",
)
assert(
  commands(deploy_jobs.fetch("admin_web")).include?("node --test src/frontend/licensing/tests/server.test.mjs"),
  "validation must exercise the admin web runtime server",
)

images_job = deploy_jobs.fetch("images")
images_commands = commands(images_job)
assert(
  images_job.fetch("if").include?("github.event_name == 'pull_request'"),
  "validation images are built for pull requests only; releases publish their own",
)
assert(
  images_job.dig("strategy", "matrix", "image").to_s.include?("needs.changes.outputs.images"),
  "validation images must come from change detection",
)
assert(
  images_commands.include?("matrix.image.dockerfile") && !images_commands.include?("--push"),
  "pull-request validation must build the selected Dockerfile without publishing it",
)
%w[
  src/backend/intelibill/Dockerfile
  src/frontend/intelibill/Dockerfile
  src/backend/intelibill/Dockerfile.migrate
  src/backend/licensing/Dockerfile
  src/backend/licensing/Dockerfile.migrate
  src/frontend/licensing/Dockerfile
].each do |dockerfile|
  assert(
    detect_script.include?(" #{dockerfile} "),
    "pull-request validation does not build #{dockerfile}",
  )
end
%w[
  intelibill-api-pr intelibill-migrate-pr intelibill-web-pr
  intelibill-licensing-api-pr intelibill-licensing-migrate-pr intelibill-admin-web-pr
].each do |local_tag|
  assert(detect_script.include?(local_tag), "pull-request validation does not build #{local_tag}")
end
image_scan = Array(images_job["steps"]).find do |step|
  step.fetch("uses", "").start_with?("aquasecurity/trivy-action@")
end
assert(
  !image_scan.nil? && image_scan.dig("with", "image-ref").to_s.include?("matrix.image.tag"),
  "pull-request validation does not scan the image it built",
)
assert(image_scan.dig("with", "exit-code") == "1", "pull-request image scan must fail the run")
deploy_jobs.each do |job_name, job|
  next if validation_job_names.include?(job_name) || job_name == "validate"

  job_text = commands(job)
  %w[src/backend/licensing src/frontend/licensing licensing-api licensing-migrate admin-web].each do |marker|
    assert(
      !job_text.include?(marker),
      "#{job_name} publishes or deploys licensing/admin output (#{marker}); P01-05 adds validation only",
    )
  end
end
assert(
  (deploy.fetch("env", {}) || {}).keys.none? { |key| key.match?(/LICENSING|ADMIN/) },
  "release workflow must not declare licensing/admin registry images",
)
badges_job = deploy_jobs.fetch("badges")
assert(badges_job["needs"] == "validate", "badge publication must follow validation")
assert(
  badges_job.fetch("if").include?("github.event_name == 'push'"),
  "badge publication must run only for main pushes",
)
assert(
  badges_job.fetch("if").include?("needs.validate.result == 'success'"),
  "badge publication must follow a successful validation gate",
)
assert(
  commands(badges_job).include?("backend-projects.json"),
  "the backend badge must keep counts for suites a scoped run did not test",
)
assert(
  badges_job.dig("permissions", "contents") == "write",
  "badge publication requires narrowly scoped contents write permission",
)
assert(
  deploy_jobs.dig("dev", "needs") == release_images.keys,
  "dev release must follow all parallel image builds",
)
assert(
  deploy_jobs.dig("prod", "needs") == [*release_images.keys, "dev"],
  "prod release must follow the same image builds and dev",
)
%w[dev prod].each do |environment|
  assert(
    deploy_jobs.fetch(environment).fetch("if").include?("!cancelled()"),
    "#{environment} release must not be skipped because an unchanged project was not validated",
  )
end
assert(deploy_jobs.dig("dev", "environment") == "dev", "dev environment missing")
assert(deploy_jobs.dig("prod", "environment") == "prod", "prod gate missing")
release_group = deploy.dig("concurrency", "group").to_s
assert(
  release_group.include?("application-release") &&
    release_group.include?("github.event.pull_request.number"),
  "release concurrency missing",
)
assert(
  deploy.dig("concurrency", "cancel-in-progress") == false,
  "production releases must not cancel in progress",
)
deploy_commands = deploy_jobs.values.map { |job| commands(job) }.join("\n")
assert(deploy_commands.include?("vars.AZURE_CLIENT_ID_DEPLOY"), "deploy identity missing")
assert(!deploy_commands.include?("vars.AZURE_CLIENT_ID_INFRA"), "release uses infra identity")
assert(deploy_jobs.to_s.include?("GHCR_PUBLIC"), "public-package deployment gate missing")

%w[dev prod].each do |environment|
  job_commands = commands(deploy_jobs.fetch(environment))
  migration_index = job_commands.index("containerapp job start")
  api_update_index = job_commands.index("containerapp update")
  assert(!migration_index.nil?, "#{environment} migration command missing")
  assert(!api_update_index.nil?, "#{environment} API update missing")
  assert(migration_index < api_update_index, "#{environment} deploys before migration")
  release_images.each_value do |image|
    assert(
      job_commands.include?(image.fetch("consumer")),
      "#{environment} does not consume #{image.fetch("consumer")}",
    )
  end
  assert(
    job_commands.include?("wait-for-container-app-job.sh"),
    "#{environment} does not wait for migration",
  )
  assert(
    job_commands.include?("wait-for-container-app-revisions.sh"),
    "#{environment} does not smoke revisions",
  )
end

puts "Workflow contracts passed."
RUBY
