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

[plan, apply, deploy].each do |workflow|
  workflow.fetch("jobs").each_value do |job|
    checkout_steps = Array(job["steps"]).select do |step|
      step.is_a?(Hash) && step.fetch("uses", "").start_with?("actions/checkout@")
    end
    checkout_steps.each do |step|
      assert(
        step.dig("with", "persist-credentials") == false,
        "checkout must not persist a workflow token into the build context",
      )
    end
  end
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
release_build = deploy_jobs.fetch("build")
assert(
  release_build.fetch("if").include?("github.event_name != 'pull_request'"),
  "pull-request code must not enter the package-write job",
)
assert(
  release_build.dig("permissions", "packages") == "write",
  "release build requires package write permission",
)
validation_commands = commands(deploy_jobs.fetch("validate"))
%w[
  src/backend/Dockerfile
  src/frontend/Dockerfile
  src/backend/Dockerfile.migrate
].each do |dockerfile|
  assert(
    validation_commands.include?(dockerfile),
    "pull-request validation does not build #{dockerfile}",
  )
end
assert(deploy_jobs.dig("dev", "needs") == "build", "dev release must follow build")
assert(
  deploy_jobs.dig("prod", "needs") == %w[build dev],
  "prod release must follow the same build and dev",
)
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
build_outputs = deploy_jobs.dig("build", "outputs")
%w[api-image web-image migrate-image].each do |output|
  assert(build_outputs.key?(output), "build output #{output} missing")
end
login_step = deploy_jobs.fetch("build").fetch("steps").find do |step|
  step.fetch("name", "") == "Login to GHCR"
end
assert(!login_step.nil?, "GHCR login step missing")
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
  %w[api-image web-image migrate-image].each do |output|
    assert(
      job_commands.include?("needs.build.outputs.#{output}"),
      "#{environment} does not consume #{output}",
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

puts "Phase 11 workflow contracts passed."
RUBY
