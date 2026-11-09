# Owner-controlled till setup

## Approved intent

Add the owner-facing shop enablement recommended in the conversation. Put it in the existing Manage shop UI and match its typography, cards, form controls, confirmations and responsive layout. Keep the backend release gate separate. Owners configure; managers and staff run daily sessions.

## Behavior and boundaries

- Manage shop gets a Tills & day close card with current status, owner-only enable/disable confirmation, plain-language consequences and a link to daily till operations. Only the active selected shop may be changed; another shop shows a switch-shop hint.
- Server availability still requires TillSessions.Enabled and exact AllowedShopIds membership. The UI cannot bypass deployment eligibility. Unavailable, shop-disabled and enabled are separate states, never a silent successful toggle.
- Store an explicit shop preference, default off for new/unconfigured shops. Migration preserves enabled tracking for shops with existing till facts. Backend gate remains an emergency stop; owners cannot change it.
- Enable requires owner authorization, observed setting version, an audit reason and explicit acknowledgement that new supported transactions require a same-day cashier session. No drawer/session is invented automatically.
- Disable requires live owner authorization and observed version; refuse open/overdue sessions, awaiting-review closes and known unposted offline submissions/review cases. Require explicit confirmation that all devices' offline queues were checked: the server cannot inspect browser-local queues. Historical facts, readers and authorized replay remain intact.
- Serialize setting changes and till opening using the same shop catalog lock and reload effective settings after acquisition. Avoid cached settings permitting a new session after disable. Every existing source uses the persisted effective gate, including offline/reconciliation paths. No legacy payload or recorded invoice is rewritten.
- A lost settings response is recovered through an authoritative GET; do not show success from an optimistic switch. Version conflict requires refresh. Scope changes ignore obsolete responses and cannot apply a selected-shop request under another scope.
- Additive schema only. Apply the new migration before the new API; no automatic migration or live enablement/deployment is part of this task.
- Web only; no mobile enablement or mixed tender.

## Contract

GET /api/shops/{shopId}/till-settings reads the active shop's setup and readiness. PUT on that route is owner-only, active-shop-scoped and uses { enabled, expectedVersion, reason, confirmed }. Response: { shopId, available, enabled, version, openSessionCount, awaitingReviewCount, pendingOfflineCount }. Version is an explicit monotonic integer, zero for an unconfigured shop. PUT returns the saved authoritative response. Use existing ErrorOr errors and safe localized error presentation.

The exact entity/repository decomposition is an implementation choice; no new generic framework or dependencies. Preserve migration/RLS and authorization conventions, and audit actor/time/from/to/reason for each actual setting change.

## Verification

Backend: owner vs manager/staff, cross-shop denial, disabled release gate, fresh opt-in, migration preservation, stale versions, blockers, concurrent opening vs disabling, source enforcement, historical reading/replay. Frontend: unavailable/read-only states, enable/disable acknowledgements, blockers, unknown responses, selected-shop/scope changes and localization. Build and inspect real rendered desktop/mobile Manage shop and till navigation with mocked APIs; retain screenshots. Run focused suites plus relevant backend/frontend regressions and production build.

## Builder-check decisions

Readiness GET is Owner/Manager-only; staff use the existing capability endpoint. A shared catalog gate must span every source's disabled/enabled decision through transaction commit, not just till opening. Opening locks period → catalog → till; settings changes take exclusive catalog lock. Pending offline blockers exclude terminally reconciled envelopes even when they have no sale link. Deploy every source writer with the new policy before exposing owner configuration; keep this version as minimum rollback binary once settings are used. An early GET at the attempted PUT's old version retains unknown status and permits only an exact retry/check; a higher version establishes current state without claiming which actor changed it.
