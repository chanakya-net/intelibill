import { isScopeChangeError } from '../../../core/context/scoped-request';
import { classifyFinancialOperationFailure } from '../../../core/services/financial-operation-intent.service';
export function classifySupplierReturnFailure(
  error: unknown,
  wasUnknown: boolean,
): 'rejected' | 'unknown' {
  const problem = (error as { error?: { code?: string; title?: string } })?.error;
  if (
    wasUnknown ||
    isScopeChangeError(error) ||
    problem?.code === 'Operation.PayloadMismatch' ||
    problem?.title === 'Operation.PayloadMismatch'
  )
    return 'unknown';
  return classifyFinancialOperationFailure(error) === 'rejected' ? 'rejected' : 'unknown';
}
