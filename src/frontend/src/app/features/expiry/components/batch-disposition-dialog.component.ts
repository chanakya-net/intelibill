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
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { firstValueFrom, Subscription } from 'rxjs';
import type { ShopScope } from '../../../core/context/shop-scope';
import {
  OPERATING_ACCESS_ERROR_KEYS,
  OperatingAccessHintService,
  operatingAccessErrorKey,
} from '../../billing/status/operating-access-hint.service';
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
  imports: [
    FormsModule,
    TranslocoPipe,
    DialogModule,
    ButtonModule,
    SelectModule,
    TextareaModule,
    QuantityFieldComponent,
  ],
  templateUrl: './batch-disposition-dialog.component.html',
  styleUrls: [
    '../../inventory/components/add-product-overlay.component.scss',
    './batch-disposition-dialog.component.scss',
  ],
})
export class BatchDispositionDialogComponent implements OnChanges, OnDestroy {
  readonly service = inject(BatchDispositionDraftService);
  readonly policies = inject(ExpiryPolicyService);
  private readonly transloco = inject(TranslocoService);
  private readonly inventory = inject(InventoryService);
  private readonly access = inject(OperatingAccessHintService);
  private openingScope: ShopScope | null = null;
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
    this.openingScope = this.access.scope();
    this.current = null;
    this.history.set([]);
    this.pending.set([]);
    this.selectedRoot = '';
    void this.load();
  }
  ngOnDestroy(): void {
    this.openingScope = null;
    this.generation++;
    this.subscription.unsubscribe();
  }
  releaseDenial(): string | null {
    return this.isCurrent(this.generation) ? this.access.blockingMessage() : null;
  }
  securityDenial(): string | null {
    const key = OPERATING_ACCESS_ERROR_KEYS['Licence.SecuritySuspended'];
    return this.releaseDenial() === key ? key : null;
  }
  canSeeDetails(): boolean {
    return this.isCurrent(this.generation) && !this.securityDenial();
  }
  roots(): readonly BatchDispositionChange[] {
    if (!this.canSeeDetails()) return [];
    return this.history().filter(
      (x) => !x.rootChangeId && remainingQuarantine(x, this.history()) > 0,
    );
  }
  rootOptions(): { label: string; value: string }[] {
    return this.roots().map((root) => ({
      label: `${root.businessDate} · ${this.transloco.translate('expiry.source.' + root.sourceKind)} · ${this.remaining(root)}`,
      value: root.id,
    }));
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
  private eligibleRoot(): BatchDispositionChange | undefined {
    const root = this.roots().find((x) => x.id === this.selectedRoot);
    const batch = this.current;
    return root &&
      batch &&
      !batch.isVoided &&
      fitsQuantityScale(this.quantity, batch.quantityScale ?? 0) &&
      this.quantity > 0 &&
      this.quantity <= (batch.quarantinedQuantity ?? 0) &&
      this.quantity <= this.remaining(root)
      ? root
      : undefined;
  }
  canDispose(): boolean {
    return !!this.eligibleRoot();
  }
  canRelease(): boolean {
    if (this.releaseDenial()) return false;
    const root = this.eligibleRoot();
    const batch = this.current;
    const decision = batch?.sellability;
    const policy = this.policy;
    if (
      !root ||
      root.reasonCode === 'Expiry.Expired' ||
      !batch?.expiryDate ||
      !policy ||
      !decision ||
      !this.businessTimeZone ||
      decision.policyVersion !== policy.version ||
      decision.catalogVersion !== this.catalogVersion ||
      (!decision.allowed && decision.reasonCode !== 'InventoryBatch.InsufficientStock') ||
      (batch.manufacturingDate && batch.manufacturingDate > batch.expiryDate)
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
  private isCurrent(generation: number): boolean {
    return (
      generation === this.generation &&
      !!this.openingScope &&
      this.openingScope === this.access.scope()
    );
  }
  async load(): Promise<void> {
    if (!this.isCurrent(this.generation)) return;
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
        this.access.refresh(),
      ]);
      if (!this.isCurrent(generation) || !this.canSeeDetails()) return;
      this.history.set(history);
      this.current = batches.find((x) => x.id === batchId) ?? null;
      this.policy = policy.effectivePolicy;
      this.catalogVersion = policy.catalogVersion;
      this.businessTimeZone = zone;
      await this.refreshPending();
    } catch (error) {
      if (this.isCurrent(generation)) {
        this.access.reportRefusal(error, this.openingScope);
        this.message.set(operatingAccessErrorKey(error) ?? 'expiry.loadFailed');
      }
    } finally {
      if (this.isCurrent(generation)) this.busy.set(false);
    }
  }
  private async refreshPending(): Promise<void> {
    if (!this.canSeeDetails()) return;
    const generation = this.generation;
    try {
      const rows = await this.service.pending(this.batch.id);
      if (this.isCurrent(generation) && this.canSeeDetails()) this.pending.set(rows);
    } catch {
      if (this.isCurrent(generation)) this.message.set('expiry.storageFailed');
    }
  }
  async submit(recovery?: BatchDispositionPayload): Promise<void> {
    if (this.busy() || !this.canSeeDetails() || this.policies.role() !== 'owner') return;
    if (!recovery && this.quantityField()?.commit() === false) return;
    let payload = recovery;
    if (!payload) {
      const root = this.eligibleRoot();
      if (
        this.pending().length ||
        !root ||
        !this.current ||
        !this.reason.trim() ||
        this.reason.trim().length > 500 ||
        (this.action === 'Release' && !this.canRelease())
      ) {
        this.message.set(
          (this.action === 'Release' && this.releaseDenial()) || 'expiry.dispositionInvalid',
        );
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
      if (!this.isCurrent(generation)) return;
      this.selectedRoot = '';
      this.reason = '';
      this.message.set('expiry.dispositionSaved');
      this.changed.emit();
      await this.load();
    } catch (error) {
      if (!this.isCurrent(generation)) return;
      this.access.reportRefusal(error, this.openingScope);
      await this.refreshPending();
      if (!this.isCurrent(generation)) return;
      this.message.set(
        operatingAccessErrorKey(error) ??
          (this.pending().length ? 'expiry.pending' : 'expiry.dispositionConflict'),
      );
    } finally {
      if (this.isCurrent(generation)) this.busy.set(false);
    }
  }
  recoverPayload(value: unknown): BatchDispositionPayload {
    return value as BatchDispositionPayload;
  }
}
