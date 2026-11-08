import {
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  Output,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { DialogModule } from 'primeng/dialog';
import { firstValueFrom, Subscription } from 'rxjs';
import { InventoryService } from '../../inventory/services/inventory.service';
import type { InventoryBatchDto } from '../../inventory/services/inventory.models';
import { QuantityFieldComponent } from '../../../shared/quantity/quantity-field.component';
import { fitsQuantityScale } from '../../../shared/quantity/quantity-input';
import {
  BatchDispositionDraftService,
  BatchDispositionChange,
  BatchDispositionPayload,
  remainingQuarantine,
} from '../services/batch-disposition-draft.service';
import { ExpiryPolicyService } from '../services/expiry-policy.service';
import type { ExpiryPolicy } from '../services/expiry.models';
import { evaluateExpiry, shopBusinessDate } from '../services/expiry-policy-evaluator';
@Component({
  selector: 'app-batch-disposition-dialog',
  standalone: true,
  imports: [FormsModule, TranslocoPipe, DialogModule, QuantityFieldComponent],
  templateUrl: './batch-disposition-dialog.component.html',
})
export class BatchDispositionDialogComponent implements OnChanges, OnDestroy {
  readonly service = inject(BatchDispositionDraftService);
  readonly policies = inject(ExpiryPolicyService);
  private readonly inventory = inject(InventoryService);
  @Input({ required: true }) batch!: InventoryBatchDto;
  @Output() closed = new EventEmitter<void>();
  @Output() changed = new EventEmitter<void>();
  readonly history = signal<readonly BatchDispositionChange[]>([]);
  readonly busy = signal(false);
  readonly message = signal('');
  readonly pending = signal<Awaited<ReturnType<BatchDispositionDraftService['pending']>>>([]);
  current: InventoryBatchDto | null = null;
  policy: ExpiryPolicy | null = null;
  private catalogVersion = 0;
  private businessTimeZone = '';
  selectedRoot = '';
  action: 'Release' | 'Dispose' = 'Dispose';
  quantity = 1;
  reason = '';
  private readonly quantityField = viewChild(QuantityFieldComponent);
  private generation = 0;
  private subscription: Subscription = this.service.changes$.subscribe(() => {
    void this.refreshPending();
  });
  ngOnChanges(): void {
    void this.load();
  }
  ngOnDestroy(): void {
    this.generation++;
    this.subscription.unsubscribe();
  }
  roots(): readonly BatchDispositionChange[] {
    return this.history().filter(
      (x) => !x.rootChangeId && remainingQuarantine(x, this.history()) > 0,
    );
  }
  remaining(root: BatchDispositionChange): number {
    return remainingQuarantine(root, this.history());
  }
  selectRoot(rootId: string): void {
    if (this.pending().length) return;
    this.selectedRoot = rootId;
    this.reason = '';
    this.quantity = 1;
  }
  canRelease(): boolean {
    const root = this.roots().find((x) => x.id === this.selectedRoot);
    const batch = this.current;
    const decision = batch?.sellability;
    const policy = this.policy;
    if (
      !root ||
      root.reasonCode === 'Expiry.Expired' ||
      !batch?.expiryDate ||
      batch.isVoided ||
      !policy ||
      !decision ||
      !this.businessTimeZone ||
      decision.policyVersion !== policy.version ||
      decision.catalogVersion !== this.catalogVersion ||
      (!decision.allowed && decision.reasonCode !== 'InventoryBatch.InsufficientStock') ||
      (batch.manufacturingDate && batch.manufacturingDate > batch.expiryDate) ||
      !Number.isFinite(this.quantity) ||
      this.quantity <= 0 ||
      this.quantity > this.remaining(root)
    )
      return false;
    const day = shopBusinessDate(Date.now(), this.businessTimeZone);
    if (decision.businessDate !== day) return false;
    const clean = evaluateExpiry(
      {
        batchId: batch.id,
        itemId: batch.itemId,
        expiryDate: batch.expiryDate,
        manufacturingDate: batch.manufacturingDate,
        physicalQuantity: this.quantity,
        quarantinedQuantity: 0,
        batchVersion: batch.version,
        itemActive: true,
        isVoided: batch.isVoided,
      },
      policy,
      this.catalogVersion,
      day,
    );
    return (
      clean.allowed &&
      !clean.requiresApproval &&
      (policy.sellThroughExpiryDate ? batch.expiryDate >= day : batch.expiryDate > day)
    );
  }
  async load(): Promise<void> {
    const generation = ++this.generation;
    const batchId = this.batch.id;
    this.busy.set(true);
    this.message.set('');
    try {
      const [history, batches, policy, zone] = await Promise.all([
        firstValueFrom(this.service.history(batchId)),
        firstValueFrom(this.inventory.getInventoryBatches()),
        firstValueFrom(this.policies.read(this.batch.itemId)),
        firstValueFrom(this.policies.businessTimeZone()),
      ]);
      if (generation !== this.generation) return;
      this.history.set(history);
      this.current = batches.find((x) => x.id === batchId) ?? null;
      this.policy = policy.effectivePolicy;
      this.catalogVersion = policy.catalogVersion;
      this.businessTimeZone = zone;
      await this.refreshPending();
    } catch {
      if (generation === this.generation) this.message.set('expiry.loadFailed');
    } finally {
      if (generation === this.generation) this.busy.set(false);
    }
  }
  private async refreshPending(): Promise<void> {
    const generation = this.generation;
    try {
      const rows = await this.service.pending(this.batch.id);
      if (generation === this.generation) this.pending.set(rows);
    } catch {
      if (generation === this.generation) this.message.set('expiry.storageFailed');
    }
  }
  async submit(recovery?: BatchDispositionPayload): Promise<void> {
    if (this.busy() || this.policies.role() !== 'owner') return;
    if (!recovery && this.quantityField()?.commit() === false) return;
    let payload = recovery;
    if (!payload) {
      const root = this.roots().find((x) => x.id === this.selectedRoot);
      if (
        this.pending().length ||
        !root ||
        !this.current ||
        !this.reason.trim() ||
        this.reason.trim().length > 500 ||
        !fitsQuantityScale(this.quantity, this.current.quantityScale ?? 0) ||
        this.quantity <= 0 ||
        this.quantity > this.remaining(root) ||
        (this.action === 'Release' && !this.canRelease())
      ) {
        this.message.set('expiry.dispositionInvalid');
        return;
      }
      payload = {
        expectedVersion: this.current.version,
        quarantineChangeId: root.id,
        action: this.action,
        quantity: this.quantity,
        reason: this.reason.trim(),
      };
    }
    this.busy.set(true);
    this.message.set('');
    const generation = this.generation;
    try {
      await firstValueFrom(this.service.execute(this.batch.id, payload));
      if (generation !== this.generation) return;
      this.selectedRoot = '';
      this.reason = '';
      this.message.set('expiry.dispositionSaved');
      this.changed.emit();
      await this.load();
    } catch {
      if (generation === this.generation) {
        await this.refreshPending();
        this.message.set(this.pending().length ? 'expiry.pending' : 'expiry.dispositionConflict');
      }
    } finally {
      if (generation === this.generation) this.busy.set(false);
    }
  }
  recoverPayload(value: unknown): BatchDispositionPayload {
    return value as BatchDispositionPayload;
  }
}
