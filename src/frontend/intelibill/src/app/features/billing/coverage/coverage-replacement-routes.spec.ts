import { describe, expect, it } from 'vitest';
import { billingRoutes } from '../billing.routes';

describe('paid replacement', () => {
  it.each(['accounts/:accountId/subscriptions/:termId/replacements', 'replacement-consent/:accountId/:shopId/:replacementId'])(
    'opens %s as a dedicated merchant billing page', path => {
      expect(billingRoutes.find(route => route.path === path)?.loadComponent).toBeTypeOf('function');
    },
  );
});
