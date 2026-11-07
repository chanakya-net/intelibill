import { HttpErrorResponse } from '@angular/common/http';
import { isScopeChangeError } from '../../../core/context/scoped-request';
import { classifyFinancialOperationFailure } from '../../../core/services/financial-operation-intent.service';
import type { ReceivePurchaseOrderRequest } from './purchase-order.service';
import type { ReceivingRowError } from './receiving.models';

interface ReceivingProblem {
  code?: string;
  title?: string;
  errors?: Record<string, unknown>;
  rowErrors?: readonly {
    code: string;
    clientLineId?: string;
    lineIndex?: number;
    field?: string;
  }[];
}
export function receivingProblem(error: unknown): ReceivingProblem {
  return (error as { error?: ReceivingProblem } | null)?.error ?? {};
}
export function classifyReceivingFailure(
  error: unknown,
  wasUnknown: boolean,
): 'rejected' | 'unknown' {
  const problem = receivingProblem(error);
  if (
    problem.code === 'Operation.PayloadMismatch' ||
    problem.title === 'Operation.PayloadMismatch' ||
    isScopeChangeError(error)
  )
    return 'unknown';
  // Replay authorization happens before receipt lookup. A denied recovery says nothing about its earlier commit.
  if (wasUnknown && error instanceof HttpErrorResponse && [401, 403].includes(error.status))
    return 'unknown';
  return classifyFinancialOperationFailure(error) === 'rejected' ? 'rejected' : 'unknown';
}
export function receivingMessage(code: string, field?: string): string {
  const messages: Record<string, string> = {
    'PurchaseOrder.UserCannotMutatePurchaseOrder': 'purchaseOrders.errors.forbidden',
    'PurchaseOrder.CannotReceiveInvalidStatus': 'purchaseOrders.errors.cannotReceiveInvalidStatus',
    'PurchaseOrder.ReceiptQuantityOverRemaining':
      'purchaseOrders.errors.receiptQuantityOverRemaining',
    'Inventory.BatchNumberAlreadyExists': 'purchaseOrders.errors.batchNumberAlreadyExists',
    'PurchaseOrder.NotFound': 'purchaseOrders.errors.notFound',
    'Operation.PayloadMismatch': 'purchaseOrders.receiving.payloadMismatch',
    'PurchaseOrder.DuplicateLot': 'purchaseOrders.receiving.duplicateLot',
    'PurchaseOrder.DuplicateClientLineId': 'purchaseOrders.receiving.invalidRow',
  };
  if (messages[code]) return messages[code];
  if (field === 'quantity') return 'purchaseOrders.receiving.invalidQuantity';
  return field ? 'purchaseOrders.receiving.invalidRow' : 'purchaseOrders.errors.unableToReceive';
}
export function normalizeReceivingErrors(
  problem: ReceivingProblem,
  payload: ReceivePurchaseOrderRequest,
): ReceivingRowError[] {
  const rows: ReceivingRowError[] = [];
  const add = (code: string, field?: string, clientLineId?: string, index?: number) => {
    const path = /^Lines\[(\d+)\]\.([\w]+)$/i.exec(code);
    const rowIndex = index ?? (path ? Number(path[1]) : undefined);
    const rawField = field ?? path?.[2];
    const normalizedField = rawField ? rawField[0].toLowerCase() + rawField.slice(1) : undefined;
    const id =
      clientLineId ?? (rowIndex === undefined ? undefined : payload.lines[rowIndex]?.clientLineId);
    rows.push({
      code,
      field: normalizedField,
      clientLineId: id,
      messageKey: receivingMessage(code, normalizedField),
    });
  };
  for (const row of problem.rowErrors ?? [])
    add(row.code, row.field, row.clientLineId, row.lineIndex);
  for (const code of Object.keys(problem.errors ?? {})) {
    if (/^Lines\[\d+\]\./i.test(code) && !rows.some((row) => row.code === code)) add(code);
  }
  return rows;
}
