/**
 * Seat usage the API attaches to a `Licence.MemberCapacityReached` refusal of a reinstatement.
 * Only the shop owner's membership edits carry it; an invitee's refusal never does.
 */
export interface MemberCapacity {
  readonly memberAllowance: number;
  readonly activeMembers: number;
}

export const MEMBER_CAPACITY_REACHED = 'Licence.MemberCapacityReached';

/** Reads `memberCapacity` from a problem body, refusing anything that is not two whole counts. */
export function readMemberCapacity(problem: unknown): MemberCapacity | null {
  const value = (problem as { memberCapacity?: unknown } | null | undefined)?.memberCapacity;
  if (typeof value !== 'object' || value === null) {
    return null;
  }

  const { memberAllowance, activeMembers } = value as Record<string, unknown>;
  return isCount(memberAllowance) && memberAllowance > 0 && isCount(activeMembers)
    ? { memberAllowance, activeMembers }
    : null;
}

function isCount(value: unknown): value is number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0;
}
