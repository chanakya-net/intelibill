import { InjectionToken, type Signal } from '@angular/core';

export type SessionState = 'unauthenticated' | 'authenticated';

export interface SessionPort {
  readonly state: Signal<SessionState>;
  readonly capabilities: Signal<readonly string[]>;
}

export const SESSION_PORT = new InjectionToken<SessionPort>('intelibill.session-port');
