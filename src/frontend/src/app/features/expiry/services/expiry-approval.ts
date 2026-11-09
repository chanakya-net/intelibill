import type { ExpiryApproval, SaleExpiryLineDecision } from './expiry.models';
/** Quantities have at most three decimals. Integer thousandths avoid duplicate-line float drift. */
export function buildExpiryApprovals(
  decisions: readonly SaleExpiryLineDecision[],
  lines: readonly { inventoryBatchId: string; quantity: number }[],
  canApprove: boolean,
  reason: string,
): readonly ExpiryApproval[] | null {
  if (decisions.some((x) => !x.decision.allowed)) return null;
  const required = decisions.filter((x) => x.decision.requiresApproval);
  if (!required.length) return [];
  if (!canApprove || !reason.trim() || reason.trim().length > 500) return null;
  const groups = new Map<string, { units: number; row: SaleExpiryLineDecision }>();
  for (const row of required) {
    const current = groups.get(row.inventoryBatchId);
    if (
      current &&
      (current.row.batchVersion !== row.batchVersion ||
        current.row.decision.policyVersion !== row.decision.policyVersion ||
        current.row.decision.catalogVersion !== row.decision.catalogVersion)
    )
      return null;
    groups.set(row.inventoryBatchId, {
      row,
      units: (current?.units ?? 0) + Math.round(row.quantity * 1000),
    });
  }
  const approvals: ExpiryApproval[] = [];
  for (const [id, group] of groups) {
    const units = lines
      .filter((x) => x.inventoryBatchId === id)
      .reduce((n, x) => n + Math.round(x.quantity * 1000), 0);
    if (!units || units !== group.units || !group.row.batchVersion) return null;
    approvals.push({
      inventoryBatchId: id,
      quantity: units / 1000,
      batchVersion: group.row.batchVersion,
      policyVersion: group.row.decision.policyVersion,
      catalogVersion: group.row.decision.catalogVersion,
      reason: reason.trim(),
    });
  }
  return approvals;
}
