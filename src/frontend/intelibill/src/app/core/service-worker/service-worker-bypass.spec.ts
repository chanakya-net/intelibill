import { describe, expect, it } from 'vitest';

import { classifyApiRequest, withServiceWorkerBypassQuery } from './service-worker-bypass';

describe('classifyApiRequest', () => {
  describe('production: API proxied on the page origin', () => {
    const base = '/api';
    const page = 'https://shop.test/sales/new';

    it.each(['/api/customers', 'https://shop.test/api/sales/123?include=lines', '/api'])(
      'sends %s as an API request around the worker',
      (url) => {
        expect(classifyApiRequest(url, base, page)).toEqual({ isApiRequest: true, bypassServiceWorker: true });
      },
    );

    it.each([
      '/assets/i18n/en-IN.json',
      'assets/i18n/hi-IN.json',
      '/apis/customers',
      '/hubs/products/negotiate',
      'https://cdn.test/api/customers',
      'https://cdn.example.com/logo.png',
    ])('treats %s as not the API', (url) => {
      expect(classifyApiRequest(url, base, page)).toEqual({ isApiRequest: false, bypassServiceWorker: false });
    });
  });

  describe('development: API on its own origin', () => {
    const base = 'http://localhost:5277/api';
    const page = 'http://localhost:4200/';

    it('sends the API request with credentials but without the CORS-costly header', () => {
      expect(classifyApiRequest('http://localhost:5277/api/sales', base, page)).toEqual({
        isApiRequest: true,
        bypassServiceWorker: false,
      });
    });

    it('does not treat the page origin as the API', () => {
      expect(classifyApiRequest('/api/sales', base, page)).toEqual({ isApiRequest: false, bypassServiceWorker: false });
    });
  });

  it('tolerates a trailing slash on the base', () => {
    expect(classifyApiRequest('/api/customers', '/api/', 'https://shop.test/')).toEqual({
      isApiRequest: true,
      bypassServiceWorker: true,
    });
  });

  it('refuses what it cannot parse', () => {
    expect(classifyApiRequest('http://[bad', '/api', 'https://shop.test/')).toEqual({
      isApiRequest: false,
      bypassServiceWorker: false,
    });
  });
});

describe('withServiceWorkerBypassQuery', () => {
  it('adds the parameter', () => {
    expect(withServiceWorkerBypassQuery('/api/ping')).toBe('/api/ping?ngsw-bypass=true');
  });

  it('appends to an existing query', () => {
    expect(withServiceWorkerBypassQuery('/api/items/stream?since=1')).toBe('/api/items/stream?since=1&ngsw-bypass=true');
  });
});
