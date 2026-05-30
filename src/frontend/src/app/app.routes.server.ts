import { RenderMode, ServerRoute } from '@angular/ssr';

export const serverRoutes: ServerRoute[] = [
  {
    // OAuth callbacks must run entirely in the browser — they read sessionStorage,
    // consume a one-time state token, and exchange an authorization code.
    // Server-side rendering would consume the state/code before the browser gets a
    // chance to handle it, causing the exchange to fail during hydration.
    path: 'auth/callback',
    renderMode: RenderMode.Client,
  },
  {
    path: '**',
    renderMode: RenderMode.Server,
  },
];
