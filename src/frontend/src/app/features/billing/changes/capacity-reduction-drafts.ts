import { Injectable } from '@angular/core';
import { isHash, isUuid, type CheckoutScope } from '../checkout/checkout.models';
import type {
  CreateCapacityReductionPreview, CreateReductionOrder, ResolveCapacityReduction, ScheduleCapacityReduction,
} from './capacity-reduction.service';

export interface CapacityReductionDraft {
  readonly schemaVersion: 1;
  readonly preview?: CreateCapacityReductionPreview;
  readonly schedule?: ScheduleCapacityReduction;
  readonly order?: { readonly scheduleId: string; readonly command: CreateReductionOrder };
  readonly resolve?: { readonly scheduleId: string; readonly command: ResolveCapacityReduction };
}

export class InvalidCapacityReductionDraft extends Error {}

const prefix = 'intelibill.billing.capacity-reduction.v1:';

@Injectable({ providedIn: 'root' })
export class CapacityReductionDrafts {
  key(scope: CheckoutScope, term: string): string {
    return prefix + JSON.stringify([scope.userId, scope.shopId, scope.account, term]);
  }

  read(scope: CheckoutScope, term: string): CapacityReductionDraft | null {
    const raw = localStorage.getItem(this.key(scope, term));
    if (raw === null) return null;
    let value: CapacityReductionDraft;
    try {
      value = JSON.parse(raw) as CapacityReductionDraft;
    } catch {
      throw new InvalidCapacityReductionDraft();
    }
    if (value?.schemaVersion !== 1 || !this.isValid(value))
      throw new InvalidCapacityReductionDraft();
    return value;
  }

  savePreview(scope: CheckoutScope, term: string, command: CreateCapacityReductionPreview): Promise<CreateCapacityReductionPreview> {
    return this.update(scope, term, saved => {
      if (saved?.preview) return { next: saved, result: saved.preview };
      return { next: { schemaVersion: 1, preview: command }, result: command };
    });
  }

  saveSchedule(scope: CheckoutScope, term: string, command: ScheduleCapacityReduction): Promise<ScheduleCapacityReduction> {
    return this.update(scope, term, saved => {
      if (saved?.schedule) return { next: saved, result: saved.schedule };
      return { next: { ...(saved ?? { schemaVersion: 1 }), schedule: command }, result: command };
    });
  }

  saveOrder(scope: CheckoutScope, term: string, scheduleId: string, snapshotHash: string): Promise<CreateReductionOrder> {
    return this.update(scope, term, saved => {
      if (saved?.order?.scheduleId === scheduleId && saved.order.command.snapshotHash === snapshotHash)
        return { next: saved, result: saved.order.command };
      const command: CreateReductionOrder = { operationKey: crypto.randomUUID(), snapshotHash };
      return { next: { ...(saved ?? { schemaVersion: 1 }), order: { scheduleId, command } }, result: command };
    });
  }

  saveResolve(scope: CheckoutScope, term: string, scheduleId: string, expectedGeneration: number,
    snapshotHash: string): Promise<ResolveCapacityReduction> {
    return this.update(scope, term, saved => {
      if (saved?.resolve?.scheduleId === scheduleId && saved.resolve.command.expectedGeneration === expectedGeneration)
        return { next: saved, result: saved.resolve.command };
      const command: ResolveCapacityReduction = { operationKey: crypto.randomUUID(), expectedGeneration, snapshotHash };
      return { next: { ...(saved ?? { schemaVersion: 1 }), resolve: { scheduleId, command } }, result: command };
    });
  }

  clearSection(scope: CheckoutScope, term: string, section: 'preview' | 'schedule' | 'order' | 'resolve'): Promise<void> {
    const key = this.key(scope, term);
    return navigator.locks.request(key, () => {
      const saved = this.read(scope, term);
      if (!saved) return;
      const next: CapacityReductionDraft = {
        schemaVersion: 1,
        preview: section === 'preview' ? undefined : saved.preview,
        schedule: section === 'schedule' ? undefined : saved.schedule,
        order: section === 'order' ? undefined : saved.order,
        resolve: section === 'resolve' ? undefined : saved.resolve,
      };
      if (!next.preview && !next.schedule && !next.order && !next.resolve) localStorage.removeItem(key);
      else localStorage.setItem(key, JSON.stringify(next));
    });
  }

  discardAll(scope: CheckoutScope, term: string): Promise<void> {
    const key = this.key(scope, term);
    return navigator.locks.request(key, () => { localStorage.removeItem(key); });
  }

  private update<T>(scope: CheckoutScope, term: string,
    mutate: (saved: CapacityReductionDraft | null) => { next: CapacityReductionDraft; result: T }): Promise<T> {
    const key = this.key(scope, term);
    return navigator.locks.request(key, () => {
      const saved = this.read(scope, term);
      const { next, result } = mutate(saved);
      localStorage.setItem(key, JSON.stringify(next));
      return result;
    });
  }

  private isValid(d: CapacityReductionDraft): boolean {
    if (d.preview && (!isUuid(d.preview.operationKey) || !isUuid(d.preview.targetPlanVersionId) ||
      !Number.isSafeInteger(d.preview.targetCapacity) || !Number.isSafeInteger(d.preview.observedCapacity) ||
      !Number.isSafeInteger(d.preview.observedMemberAllowance) || !Number.isFinite(Date.parse(d.preview.observedPaidThrough))))
      return false;
    if (d.schedule && (!isUuid(d.schedule.operationKey) || !isUuid(d.schedule.previewId) ||
      !isHash(d.schedule.previewSnapshotHash) || !Array.isArray(d.schedule.retainedShopIds) ||
      d.schedule.retainedShopIds.length === 0 || !d.schedule.retainedShopIds.every(isUuid)))
      return false;
    if (d.order && (!isUuid(d.order.scheduleId) || !isUuid(d.order.command.operationKey) || !isHash(d.order.command.snapshotHash)))
      return false;
    if (d.resolve && (!isUuid(d.resolve.scheduleId) || !isUuid(d.resolve.command.operationKey) ||
      !Number.isSafeInteger(d.resolve.command.expectedGeneration) || !isHash(d.resolve.command.snapshotHash)))
      return false;
    return true;
  }
}
