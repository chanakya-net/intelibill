import type { InventoryInboundSubmissionChunk } from '../../../core/storage/inventory-draft-indexeddb.service';
import { OPERATING_ACCESS_ERROR_KEYS } from '../../billing/status/operating-access-hint.service';
import type { AddInventoryBatchFailedRow, AddInventoryBatchResponse } from './inventory.models';

export function assertIntakeResponse(
  chunk: InventoryInboundSubmissionChunk,
  response: AddInventoryBatchResponse,
): void {
  const pending = new Set(chunk.rows.map(({ clientRowId }) => clientRowId));
  if (!Array.isArray(response?.succeeded) || !Array.isArray(response?.failed) ||
      response.requestedCount !== pending.size || response.successCount !== response.succeeded.length ||
      response.failedCount !== response.failed.length ||
      !response.succeeded.every(row => row?.result &&
        [row.result.itemId, row.result.inventoryBatchId, row.result.stockTransactionId].every(
          id => typeof id === 'string' && id.length > 0,
        ) && pending.delete(row.clientRowId)) ||
      !response.failed.every(row => pending.delete(row?.clientRowId) && typeof row.retryable === 'boolean' &&
        Array.isArray(row.errors) && row.errors.length > 0 &&
        row.errors.every((error: { code?: unknown; description?: unknown }) => typeof error?.code === 'string' && typeof error.description === 'string')) ||
      pending.size) {
    throw new Error('The stock submission response did not account for every row.');
  }
}

export function needsIntakeRecovery(row: AddInventoryBatchFailedRow): boolean {
  return row.retryable || row.errors.some(error => Object.hasOwn(OPERATING_ACCESS_ERROR_KEYS, error.code));
}
