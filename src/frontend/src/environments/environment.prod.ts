export const environment = {
  production: true,
  // Relative, resolved against whichever origin served the page. The web server
  // proxies /api and /hubs to API_ORIGIN, so no API hostname is compiled into
  // the bundle and moving the API needs no rebuild.
  apiBaseUrl: '/api',
  hubBaseUrl: '',
};
