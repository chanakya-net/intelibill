# Reviewed trial extensions

A Licensing Manager can preview and approve an exact, later trial deadline in Provider Console → Account access status. The preview shows the originating trial window, current end, proposed end, reason and review reference. Enter the deadline in IST. Seconds can include up to six decimal digits. Approval and operation recovery require recent MFA.

The original trial and coverage term remain immutable. An approved extension appends an operational revision and a provider-only receipt. Each shop's revision advances under the account fence. Confirmation binds the reviewed facts and operation key to the administrator, so a replay returns the same receipt even after expiry. The console saves approvals in IndexedDB before sending and restores the original operation across reloads and tabs.

The existing merchant status endpoint reports the effective end on the same Trial term. Its deadline is end-exclusive and carries no grace or commercial facts. A reviewed exception can revive an expired trial or exceed the account's concurrent-trial cap with disclosure in the preview. Its effective interval counts toward the cap for standard starts. Status remains observe-only.

Apply `20261116190000_AddTrialExtensions` through the licensing migration job before deploying the API. The migration can roll back while both new tables are empty and refuses rollback once extension evidence exists.
