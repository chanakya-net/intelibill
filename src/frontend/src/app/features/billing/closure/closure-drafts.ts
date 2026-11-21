import { Injectable } from '@angular/core';

export interface OwnerBoundClosureDraft<T> {
  readonly input: T;
  readonly operationKey: string;
  readonly settled: boolean;
}

@Injectable({ providedIn: 'root' })
export class ClosureDrafts {
  read<T>(scope: string): OwnerBoundClosureDraft<T> | null {
    const raw = localStorage.getItem(this.key(scope));
    if (raw === null) return null;
    const draft = JSON.parse(raw) as OwnerBoundClosureDraft<T> | null;
    if (!draft?.input || !/^[0-9a-f-]{36}$/i.test(draft.operationKey) || typeof draft.settled !== 'boolean')
      throw new Error('Invalid closure draft');
    return draft;
  }

  prepare<T>(scope: string, operationKey: string, input: T, replace?: (draft: OwnerBoundClosureDraft<T>) => boolean) {
    return navigator.locks.request(this.key(scope), () => {
      const previous = this.read<T>(scope);
      if (previous && !replace?.(previous)) return previous;
      const draft: OwnerBoundClosureDraft<T> = { operationKey, input, settled: false };
      localStorage.setItem(this.key(scope), JSON.stringify(draft));
      return draft;
    });
  }

  finish(scope: string, operationKey: string, settled: boolean) {
    return navigator.locks.request(this.key(scope), () => {
      const key = this.key(scope);
      const previous = this.read(scope);
      if (previous?.operationKey !== operationKey || previous.settled) return;
      if (settled) localStorage.setItem(key, JSON.stringify({ ...previous, settled: true }));
      else localStorage.removeItem(key);
    });
  }

  private key(scope: string) {
    return 'intelibill.billing.closure.v1:' + scope;
  }
}
