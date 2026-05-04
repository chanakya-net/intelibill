# ISSUES
GitHub issues are provided in context after they have been pulled from GitHub.
Parse the issue title, body, labels, comments, and any linked context to understand the open work.
Treat the issue data already present in the prompt as the source of truth for issue selection and planning.
Do not use the Superpower skill for this run.
If additional GitHub data is required, use the `gh` CLI instead of GitHub MCP or other GitHub issue integrations.
If issue details are already included in the prompt, do not call tools to retrieve the same issue again unless you need fresh data that is not already present.
Work only on ready-for-agent issues, not HITL issues.
You may also be given a file containing the last few commits. Review it to understand what has already been done.
Continue selecting and completing ready tasks until there are no more ready ready-for-agent tasks left for this run.
If all ready-for-agent tasks are complete, output `<promise>NO MORE TASKS</promise>`.

# OPERATING MODE
You may work as a single agent or as a coordinator managing multiple agents.
Prefer multiple agents when there are several ready ready-for-agent issues that can be worked on safely in parallel.
Use a single agent when the work is tightly coupled, sequencing-sensitive, or concentrated in the same files or modules.
Do not stop after completing one issue if other ready tasks remain.
After finishing a task or a safe parallel batch, immediately reassess the remaining issue list and continue.
If your runtime supports subagents, workers, background agents, or parallel delegated tasks, use them for independent ready work.
If your runtime does not support true subagents, simulate the same discipline by planning a batch and executing each issue sequentially with explicit ownership boundaries.
Never use multiple agents only for speed if the work shares state, files, migrations, architectural decisions, or test fixtures that require one coherent implementation.

# TASK SELECTION
Select the next unit of work. This may be a single issue or a batch of issues for parallel execution.
Before selecting any task, confirm that all of its dependencies and prerequisite tasks are already complete.
If a task depends on another open or in-progress task, do not start it yet.
Instead, select the dependency first or mark the dependent task as blocked.
Prioritize tasks in this order:
1. Critical bugfixes
2. Development infrastructure
Getting tests, types, tooling, and development scripts in place is an important precursor to building features safely.
3. Tracer bullets for new features
Tracer bullets are small end-to-end slices that go through all relevant layers so you can validate the approach early before scaling the implementation.
TL;DR: build a tiny end-to-end slice first, then expand it.
4. Polish and quick wins
5. Refactors

# PARALLEL PLANNING
When multiple ready-for-agent issues are available, build a small execution batch:
1. Identify which issues are ready now, meaning every prerequisite and dependency is already done.
2. Group only issues with minimal file overlap and low coordination cost.
3. Prefer vertical slices with clear ownership boundaries.
4. Keep one issue per agent.
5. Avoid batching issues that edit the same files unless one agent is explicitly designated as the owner of those files.
6. Do not assign dependent issues to parallel agents unless the upstream issue has already been completed before this iteration starts.
7. Keep the first batch small. Prefer 2-3 agents unless the ownership boundaries are obvious and verification is cheap.
8. Keep one coordinator responsible for final integration, code review, tests, commits, and issue updates.

For each selected issue, define:
- the GitHub issue number and title
- why it is ready now
- dependencies or risks
- proof that dependencies are complete
- expected file or module ownership
- shared architecture constraints each agent must follow
- verification steps

# COORDINATION
If you spawn multiple agents, act as the coordinator:
- assign each agent a single issue
- give each agent explicit file or module ownership
- tell agents not to duplicate work or overwrite another agent's changes
- keep shared decisions centralized in the coordinator
- resolve conflicts before merging work
- make each agent report changed files, design decisions, tests run, and unresolved risks
- review each agent's changes before accepting them
- reject or revise agent work that violates existing architecture, duplicates domain logic, skips required tests, or edits outside its ownership

Agent instructions must include:
- issue number and goal
- exact files, folders, or modules owned by that agent
- files, folders, or modules the agent must not edit
- relevant domain model, naming, layering, and API conventions discovered during exploration
- expected verification command for that issue
- instruction to keep changes minimal and compatible with other agents

If a task turns out to be blocked by another in-progress issue or an unfinished dependency, stop parallelizing that branch and reorder the queue.

# EXPLORATION
Explore the repo before making changes.
For parallel batches, do a short shared exploration first so each agent starts with the same understanding of architecture and constraints.
Read the nearest existing implementation before writing new code.
Identify the current architecture, domain terms, boundaries, and conventions before choosing an approach.
Prefer existing patterns over new abstractions.
Do not invent a new service, folder, API shape, state model, validation style, or persistence pattern unless the issue requires it and the reason is documented.
Look for:
- domain entities and value objects already used for the workflow
- backend layering, dependency direction, and transaction boundaries
- frontend routing, state management, component composition, and form patterns
- validation, authorization, error handling, logging, and test conventions
- existing helpers that should be reused

# IMPLEMENTATION
Use the TDD workflow or available tdd skill to complete the task.
Each agent should implement only its assigned issue.
The coordinator should integrate results, resolve overlaps, and make sure the combined state still passes verification.
Write code that fits the existing architecture.
Keep changes scoped to the issue.
Do not bypass domain rules, duplicate business logic in UI/API layers, or couple unrelated modules to make a test pass.
If an issue exposes an architectural gap, implement the smallest compatible extension and document the decision in the issue update or commit message.

# CODE REVIEW
Before committing, review the diff as a maintainer:
- confirm the implementation matches the issue and does not include unrelated changes
- confirm architecture boundaries are preserved
- confirm naming matches existing domain language
- confirm failure paths, validation, and authorization are handled where relevant
- confirm tests prove the behavior at the right layer
- confirm generated or agent-written code is not overly broad, duplicated, or inconsistent with nearby code
- confirm no agent overwrote or reverted unrelated work

For parallel batches, review each agent diff separately first, then review the combined diff for integration bugs.

# FEEDBACK LOOPS
Before committing, run the feedback loops relevant to the issues completed in this iteration:
- `bun run test` to run the frontend tests
- `dotnet test` to run the backend tests
Also run any narrower issue-specific checks that provide fast confidence before full-suite validation.

# COMMIT
If working on a single issue, make one git commit.
If coordinating multiple agents, prefer one coherent commit per completed issue unless the issues are intentionally shipped together and the combined change is easier to review as one unit.

Each commit message must include:
1. The GitHub issue number
2. Key decisions made
3. Files changed
4. Blockers or notes for the next iteration

# THE ISSUE
When the task is complete, post an update to the GitHub issue summarizing what was finished, how it was verified, and any follow-up work.
Then close the completed GitHub issue with `gh issue close` unless the issue explicitly needs to remain open for a documented reason.
If the task is not complete, prepare an update for the GitHub issue describing what was done, what is blocked, and the recommended next step.
Do not rely on local issue files or moving files between folders.
For parallel execution, prepare a separate update for each GitHub issue.

# FINAL RULES
Do not work on HITL issues.
Do not assign the same issue to multiple agents.
Do not batch issues with significant overlap unless you explicitly define ownership and merge order.
If parallel work is unsafe, fall back to a single task.
Prefer `gh` CLI commands over GitHub MCP or other GitHub issue-fetching tools.
Keep going until all ready tasks are done, then and only then output `<promise>NO MORE TASKS</promise>`.
