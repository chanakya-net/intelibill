import { problemCode } from '@intelibill/api';
import { hasProblemCode } from '../../../shared/utils/problem-code.util';

const SOURCE_ERROR_KEYS = {
  'SaleReturn.SourceIneligible': 'sales.returns.preview.validation.sourceIneligible',
  'SaleReturn.SaleItemNotFound': 'sales.returns.preview.validation.sourceLineUnavailable',
  'SaleReturn.SaleCancelled': 'sales.returns.preview.validation.sourceCancelled',
  'SaleReturn.QuantityExceedsRemaining': 'sales.returns.preview.validation.sourceQuantityChanged',
};

export function saleReturnSourceErrorKey(error: unknown): string | undefined {
  const code = problemCode((error as { error?: unknown } | null)?.error);
  return Object.entries(SOURCE_ERROR_KEYS).find(
    ([sourceCode]) => code === sourceCode || hasProblemCode(error, sourceCode),
  )?.[1];
}

export function requiresSaleReturnSourceRefresh(messageKey: string | null): boolean {
  return Object.values(SOURCE_ERROR_KEYS).some((key) => key === messageKey);
}
