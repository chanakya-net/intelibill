import { signal } from '@angular/core';
import type { SessionPort, SessionState } from '@intelibill/api';

export const unavailableProviderSession: SessionPort = {
  state: signal<SessionState>('unauthenticated').asReadonly(),
  capabilities: signal<readonly string[]>(Object.freeze([])).asReadonly(),
};
