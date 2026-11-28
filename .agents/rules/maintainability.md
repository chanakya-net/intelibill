# Maintainability Rules

These rules apply to every change in this repository, by any agent or person. They exist because each one
was broken often enough to need a dedicated clean-up (PRs #959 to #962). Numbers are limits, not targets:
smaller is better. If a rule cannot be met, say so in the PR description with the reason; do not silently
exceed it.

The repo-root `AGENTS.md` (also reachable as `CLAUDE.md` and `GEMINI.md`) carries the short version.
Backend patterns referred to below are in `src/backend/intelibill/architecture_doc/patterns.md`.

## 1. Shape of the code

- **One statement per line.** Never `var x = F(); if (x.IsError) return x.Errors;` on one line, and never
  two property or field declarations on one line.
- **Lines of 140 characters or fewer.** When a call, constructor or declaration wraps, put one argument or
  parameter per line. Break long conditions before `&&` / `||`, and LINQ chains before each `.Method(`.
- **Methods of 60 lines or fewer; never more than 100.** A longer method becomes a short list of named
  steps. Name a step for what it decides or posts (`AuthorizePostingAsync`, `DeductStockAsync`), not for
  how (`DoStep2`, `Helper`).
- **Source files of 400 lines or fewer** (tests, generated code and EF configurations excepted). Split by
  responsibility, not by line count.
- **No nesting deeper than three levels** inside a method. Use early returns and extracted steps.
- **Comments say why**: a constraint, a lock order, a reason something must not be reordered. Never
  restate what the code does. When code moves, its comments move with it.
- **No dead code**: no template leftovers, commented-out code, unused `using`/imports, unused parameters.
- **No generated or cache files in git** (`bin/`, `obj/`, `*.lscache`, generated Wolverine handlers,
  coverage output). If a tool writes one, add it to `.gitignore` in the same change.

## 2. Dependencies and collaborators (backend)

- **Ten injected dependencies or fewer per class; never more than twelve.** Past that, move a cohesive
  piece of work, together with the repositories only it needs, into a collaborator (`patterns.md` §17).
- A collaborator has **one responsibility**, takes at least two dependencies away from its consumer (or
  one plus real logic), is `public sealed`, registered `AddScoped`, and holds no state between calls.
- **No pass-through wrappers**: a method that only forwards to one repository call is not a collaborator.
- **No facade over locks.** The licence fence, posting-period policy, expiry fence, till posting and
  row-lock calls stay in the handler's top-level method, in lock order, so the order can be read in one
  place. A method that takes a row lock says so in its name (`LockCustomerAndDecideCreditAsync`).
- **Transactions, saves and their `try`/`catch`/`finally` stay in the handler.** A collaborator never
  begins, commits, rolls back or saves.
- Never name a collaborator `*Handler` or `*Consumer`, or give it a `Handle`/`Consume` method: Wolverine
  would discover it as a message handler.
- **Do not merge two classes because they look alike.** Compare them statement by statement first. Merge
  only what is identical, or differs by at most two explicit parameters. Different write order, checks,
  error codes or lock placement means different behaviour: leave them apart.
- **No boolean-flag parameters that switch behaviour**, no speculative interfaces, no base classes
  introduced only to share a few lines.

## 3. Errors

- **Every error code is defined exactly once**, in `Intelibill.Application/Common/Errors/Errors.<Group>.cs`
  (Domain-owned errors stay in Domain). Code `Group.Name` lives at `Errors.Group.Name`.
- **No inline `Error.Validation("Code", "message")` literals** in Application, Infrastructure or Api.
  Reference the `Errors` member.
- **One code, one kind, one message.** Before adding an error, search for the code. If it exists with
  another message, reuse it or choose a new code; never return the same code with different text.
- **Check `IsError` before `.Value`** on every `ErrorOr<T>`, including domain factory results. `.Value` on
  an error throws.
- Errors are results, not exceptions: no `throw` for an expected business refusal.

## 4. Layering

- `Domain ← Application ← Infrastructure ← Api`. **Application never names an ORM or driver type**
  (`Microsoft.EntityFrameworkCore`, `Npgsql`, `DbUpdateException`). The Application project has no EF Core
  package reference; do not add one.
- Recognise a failed save through the unit of work:
  `catch (Exception ex) when (unitOfWork.IsConcurrencyConflict(ex))` or `IsWriteFailure(ex)`.
  Keep catch clauses in their original order; put the type test first in a filter.
- **Read the clock once per operation** and pass the value down. Two reads can land on different business
  days around midnight.
- Decide once, under the lock. Do not evaluate a condition before taking a lock and again after it unless
  the second evaluation is the one that is used and the first is only a cheap early refusal.

## 5. Web (Angular)

- New components use `ChangeDetectionStrategy.OnPush`.
- A component class is 300 lines or fewer. A template longer than about 40 lines goes in its own `.html`
  file; styles in their own file.
- No bare `.subscribe(` in components: use signals, the `async` pipe, `toSignal`, or
  `takeUntilDestroyed()`. State changes go through the store or a facade, not through component fields.
- No `any`, `@ts-ignore` or `eslint-disable` without a one-line reason next to it.
- Every user-visible string goes through Transloco, in all nine locales.

## 6. Mobile (Flutter)

- State classes are Freezed (`@freezed`), not hand-written classes with long constructors.
- A controller is 400 lines or fewer; split by concern (search, cart, payment, submission).
- No `// ignore:` without a one-line reason.

## 7. Tests

- Build the system under test through one helper per test class (`CreateHandler()`, `CreateSut()`), so a
  constructor change touches one place.
- A refactor never changes an assertion, an expectation, a test name or arranged data. If a test must
  change for a refactor to pass, the refactor changed behaviour.
- When a fake must behave like the real thing to reach a code path, share one helper for it (for example
  `ClassifiesFailuresLikeEf()` for substitute units of work).

## 8. Changes and commits

- **A behaviour-preserving change and a behaviour change never share a commit.** Reformatting, moving
  code and renaming go first, on their own.
- In a refactor, preserve exactly: the order of awaits, locks, reads and writes; every early return
  relative to side effects; transaction and `try`/`catch` scope; error codes and messages; the
  `CancellationToken` passed to each call; LINQ evaluation timing; decimal arithmetic and rounding order.
- If you notice a bug while refactoring, report it; fix it in a separate change.
- Verify before claiming done: build with warnings as errors, the unit tests of every layer touched, and
  the integration tests for the feature (`scripts/dev/test-integration.sh --filter ...`). A change to
  posting, locking, DI registration or shared persistence code runs the unfiltered suite.
- Update the project's `README.md` / `architecture_doc/` in the same change when structure changes.

## 9. Before you finish: checklist

1. Any line over 140 characters, or two statements on one line?
2. Any method over 60 lines, file over 400, class with more than 10 injected dependencies?
3. Any new inline error literal, or an error code that already exists with other text?
4. Any `.Value` without an `IsError` check before it?
5. Any EF Core or Npgsql type named in Application?
6. Any lock, fence, save or transaction moved out of a handler's top-level method?
7. Any unused `using`, dead code, template file or generated file added?
8. Did a refactor commit change behaviour or a test assertion?
9. Build, unit tests and the right integration tests run, with the numbers stated?
