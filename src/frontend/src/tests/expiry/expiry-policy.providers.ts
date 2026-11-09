import { EMPTY, of } from 'rxjs';
import { ExpiryPolicyService } from '../../app/features/expiry/services/expiry-policy.service';
export const expiryPolicyState = {
  defaultPolicy: {
    version: 1,
    mode: 'Ignore' as const,
    expiryRequired: false,
    sellThroughExpiryDate: true,
    warningDays: 7,
    overrideAllowed: false,
  },
  overridePolicy: null,
  effectivePolicy: {
    version: 1,
    mode: 'Ignore' as const,
    expiryRequired: false,
    sellThroughExpiryDate: true,
    warningDays: 7,
    overrideAllowed: false,
  },
  catalogVersion: 1,
};
/** A known server policy for tests of unrelated form fields. Expiry's own tests exercise the real service. */
export function knownExpiryPolicyProviders() {
  return [
    {
      provide: ExpiryPolicyService,
      useValue: {
        read: () => of(expiryPolicyState),
        businessTimeZone: () => of('Asia/Kolkata'),
        readiness: () => EMPTY,
        scopeIdentity: () => 'known-test-scope',
        role: () => 'owner',
        activeShopId: () => 'shop-1',
        changed$: EMPTY,
        invalidate: async () => {},
      },
    },
  ];
}
