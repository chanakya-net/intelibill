/**
 * A shop invitation that was opened before its invited person had signed in.
 *
 * The link's single-use token arrives in the URL fragment and is stripped from the address bar at
 * once, so it is held here — in this tab only — while they sign in. Without it, the normal
 * click-from-email path would throw the invitation away and force a second trip to the inbox.
 */
export interface PendingInvitation {
  readonly invitationId: string;
  readonly token: string;
}

const PENDING_INVITATION_KEY = 'inventory.invitations.pending';

/** Where a signed-in person is sent to finish an invitation they opened while signed out. */
export const ACCEPT_INVITATION_ROUTE = '/invitations/accept';

export function readPendingInvitation(): PendingInvitation | null {
  try {
    const stored = sessionStorage.getItem(PENDING_INVITATION_KEY);
    if (!stored) {
      return null;
    }

    const parsed = JSON.parse(stored) as Partial<PendingInvitation>;
    return parsed.invitationId && parsed.token
      ? { invitationId: parsed.invitationId, token: parsed.token }
      : null;
  } catch {
    return null;
  }
}

export function storePendingInvitation(invitation: PendingInvitation): void {
  try {
    sessionStorage.setItem(PENDING_INVITATION_KEY, JSON.stringify(invitation));
  } catch {
    // A browser that refuses session storage simply loses the sign-in detour; the link in the
    // email still works once signed in.
  }
}

export function clearPendingInvitation(): void {
  try {
    sessionStorage.removeItem(PENDING_INVITATION_KEY);
  } catch {
    // Nothing to clean up.
  }
}
