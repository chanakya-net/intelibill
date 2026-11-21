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
