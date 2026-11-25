import { describe, expect, it } from 'vitest';
import { billingRoutes } from '../billing.routes';

describe('paid slot assignment', () => {
  it.each(['accounts/:accountId/subscriptions/:termId/slots', 'slot-consent/:accountId/:shopId/:assignmentId'])(
    'opens %s as a dedicated merchant billing page', path => {
      expect(billingRoutes.find(route => route.path === path)?.loadComponent).toBeTypeOf('function');
    },
  );
});
