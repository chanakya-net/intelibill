import { HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { problemCode } from '@intelibill/api';
import type { ShopContextService } from './shop-context.service';
import type { ShopScope } from './shop-scope';

/** Raw fetch consumers do the same scoped refusal handling as the Angular interceptor. */
export async function observeShopDataRefusal(
  response: Response, context: ShopContextService, scope: ShopScope | null,
): Promise<void> {
  if (response.status !== 403 || !scope || context.scope() !== scope) return;
  const problem: unknown = await response.json().catch(() => null);
  if (problemCode(problem) === 'Licence.SecuritySuspended') context.setDataRestricted(scope, true);
}

export function readTradingAuthorityRestriction(
  response: HttpResponse<unknown> | HttpErrorResponse,
  shopId: string,
): boolean | undefined {
  if (response instanceof HttpErrorResponse) {
    const problem = response.error as { enforced?: unknown } | null;
    if (problem?.enforced !== true) return undefined;
    const code = problemCode(problem);
    if (response.status === 403) {
      if (code === 'Licence.SecuritySuspended') return true;
      if (code === 'Licence.Expired' || code === 'Licence.TradingRestricted' || code === 'Licence.PendingActivation')
        return false;
    }
    // An evaluated unavailable decision still carries the local, non-security projection.
    if (response.status === 503 && code === 'Licence.VerificationUnavailable') return false;
    return undefined;
  }

  const decision = response.body as {
    schemaVersion?: number; shopId?: string; enforced?: boolean; authorityVerifiedAt?: string; validUntil?: string;
  } | null;
  return decision?.schemaVersion === 1 && decision.shopId === shopId && decision.enforced === true &&
    Number.isFinite(Date.parse(decision.authorityVerifiedAt ?? '')) &&
    Number.isFinite(Date.parse(decision.validUntil ?? '')) ? false : undefined;
}
