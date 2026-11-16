# Entitlement synchronization status

The merchant billing status and provider account status show the saved account
and shop revisions separately from the revisions confirmed by the merchant.
Account pages show those facts for each covered shop. These views are
observe-only and use local fixtures for verification.

- **Pending** covers a queued or retrying delivery and a delivery waiting for
  the merchant's signed acknowledgement.
- **Applied** indicates that licensing has recorded an acknowledgement
  confirming both saved revisions.
- **Needs review** covers exhausted delivery attempts or an acknowledgement
  requiring review. The reference is an operational code.

The saved time comes from the frozen shop snapshot. The last successful
synchronization comes from the merchant's original apply time in the retained
acknowledgement. A newer pending or unresolved snapshot still shows the last
confirmed revisions and time. Retrying the same event preserves the original
evidence. A new verification snapshot is issued through the existing
account-fenced reconciliation path.

The additive `subscription-status-v1` fields are available with local
entitlement transport enabled after the shop has a saved snapshot. Apply the
P02-13 migrations and grants first. Update the merchant API relay before the
licensing API and web consumers when the transport is enabled. A merchant API
from before P02-14 refuses the new status values. P02-14 adds no migrations.
