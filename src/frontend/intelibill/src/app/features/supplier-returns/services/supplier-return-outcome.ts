import {
  requirePaymentOutcome,
  type FinancialOperationLookup,
} from '../../../core/services/financial-operation-intent.service';
import type {
  ReceiptReversalResult,
  SupplierReturnAction,
  SupplierReturnDetail,
  SupplierReturnMutationPayload,
} from './supplier-return.models';

type CompletionResult = SupplierReturnDetail | ReceiptReversalResult;

const operationTypes = {
  shipment: 'supplier-return',
  credit: 'supplier-return-credit',
  reversal: 'purchase-receipt-reversal',
} as const satisfies Record<SupplierReturnAction, string>;

const dashedGuid = '[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}';
const guid = new RegExp(
  `^(?:${dashedGuid}|\\{${dashedGuid}\\}|\\(${dashedGuid}\\)|[0-9a-f]{32})$`,
  'i',
);

export function completionOperationType(action: SupplierReturnAction) {
  return operationTypes[action];
}

export function guidIdentity(value: string): string {
  if (!guid.test(value)) return value;
  const digits = value.replace(/[^0-9a-f]/gi, '').toLowerCase();
  return `${digits.slice(0, 8)}-${digits.slice(8, 12)}-${digits.slice(12, 16)}-${digits.slice(16, 20)}-${digits.slice(20)}`;
}

export function requireCompletionOutcome(
  lookup: FinancialOperationLookup<CompletionResult>,
  action: SupplierReturnAction,
  target: string,
  payload: SupplierReturnMutationPayload,
): CompletionResult {
  const outcomeTarget = 'supplierId' in payload ? payload.supplierId : target;
  const response = requirePaymentOutcome(
    { ...lookup, targetId: guidIdentity(lookup.targetId) },
    completionOperationType(action),
    payload.operationKey,
    guidIdentity(outcomeTarget),
  );
  return requireCompletionResult(response, action, target, payload);
}

export function requireCompletionResult(
  response: CompletionResult,
  action: SupplierReturnAction,
  target: string,
  payload: SupplierReturnMutationPayload,
): CompletionResult {
  if (
    response &&
    (action === 'reversal'
      ? matchesReversal(response, target, payload)
      : matchesReturn(response, target, payload))
  ) {
    return response;
  }
  throw new Error('Supplier return outcome does not match the frozen source.');
}

function matchesReversal(
  response: CompletionResult,
  target: string,
  payload: SupplierReturnMutationPayload,
) {
  if ('supplierId' in payload || 'expectedVersion' in payload) return false;
  const expectedLots =
    'lines' in payload ? payload.lines.map((line) => line.receiptValueId) : [target];
  return (
    'lots' in response &&
    Array.isArray(response.lots) &&
    response.lots.every((lot) => nonempty(lot?.reversalId)) &&
    sameIds(
      response.lots.map((lot) => lot.receiptValueId),
      expectedLots,
    )
  );
}

function matchesReturn(
  response: CompletionResult,
  target: string,
  payload: SupplierReturnMutationPayload,
) {
  if (
    !('returnId' in response) ||
    !nonempty(response.returnId) ||
    !nonempty(response.returnNumber) ||
    !nonempty(response.supplierId) ||
    !Array.isArray(response.lines) ||
    !response.lines.every((line) => nonempty(line?.returnLineId))
  )
    return false;

  if ('supplierId' in payload) {
    return (
      guidIdentity(response.supplierId) === guidIdentity(payload.supplierId) &&
      (payload.creditAccepted
        ? response.status === 'CreditAccepted' && nonempty(response.credit?.creditId)
        : response.status === 'PendingSupplierCredit') &&
      sameIds(
        response.lines.map((line) => line.receiptValueId),
        payload.lines.map((line) => line.receiptValueId),
      )
    );
  }

  return (
    'expectedVersion' in payload &&
    guidIdentity(response.returnId) === guidIdentity(target) &&
    response.status === 'CreditAccepted' &&
    nonempty(response.credit?.creditId) &&
    sameIds(
      response.lines.map((line) => line.returnLineId),
      payload.lines.map((line) => line.returnLineId),
    )
  );
}

function sameIds(actual: string[], expected: string[]): boolean {
  if (!expected.length || actual.length !== expected.length || !actual.every(nonempty))
    return false;
  const ids = new Set(actual.map(guidIdentity));
  return ids.size === expected.length && expected.every((id) => ids.has(guidIdentity(id)));
}

function nonempty(value: unknown): value is string {
  return typeof value === 'string' && !!value.trim();
}
