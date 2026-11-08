import { ViewChild } from '@angular/core';
import { TillPostingFieldsComponent } from '../../../tills/components/till-posting-fields.component';
import { CommonModule } from '@angular/common';
import {
  Component,
  EventEmitter,
  Input,
  Output,
  computed,
  inject,
  signal,
  OnChanges,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { DialogModule } from 'primeng/dialog';
import { ButtonModule } from 'primeng/button';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../../../core/auth/auth.service';
import { ShopContextService } from '../../../../core/context/shop-context.service';
import type { ShopScope } from '../../../../core/context/shop-scope';
import { SaleCancellationService } from '../../services/sale-cancellation.service';
import { SaleCancellationDraftService } from '../../services/sale-cancellation-draft.service';
import type {
  SaleCancellationDraftRecord,
  SaleCancellationDto,
  SaleCancellationPreviewDto,
} from '../../services/sale-cancellation.models';
@Component({
  selector: 'app-sale-cancellation-dialog',
  standalone: true,
  imports: [
    TillPostingFieldsComponent,
    CommonModule,
    FormsModule,
    TranslocoPipe,
    DialogModule,
    ButtonModule,
  ],
  templateUrl: './sale-cancellation-dialog.component.html',
})
export class SaleCancellationDialogComponent implements OnChanges {
  @ViewChild(TillPostingFieldsComponent) tillFields!: TillPostingFieldsComponent;
  private readonly api = inject(SaleCancellationService);
  private readonly drafts = inject(SaleCancellationDraftService);
  private readonly context = inject(ShopContextService);
  private readonly auth = inject(AuthService);
  @Input() saleId = '';
  @Input() visible = false;
  @Output() readonly visibleChange = new EventEmitter<boolean>();
  @Output() readonly completed = new EventEmitter<SaleCancellationDto>();
  readonly preview = signal<SaleCancellationPreviewDto | null>(null);
  readonly record = signal<SaleCancellationDraftRecord | null>(null);
  readonly busy = signal(false);
  readonly ready = signal(false);
  readonly error = signal('');
  readonly canManage = computed(() => {
    const s = this.auth.session();
    return ['owner', 'manager'].includes(
      s?.shops.find((x) => x.shopId === s.activeShopId)?.role.toLowerCase() ?? '',
    );
  });
  reason = '';
  goodsConfirmed = false;
  cashConfirmed = false;
  private owner: ShopScope | null = null;
  private target = '';
  private generation = 0;
  private saves: Promise<void> = Promise.resolve();
  ngOnChanges() {
    if (this.visible && this.saleId) void this.restore();
  }
  private current(owner = this.owner, target = this.target, generation = this.generation) {
    return (
      !!owner &&
      this.visible &&
      this.target === target &&
      this.generation === generation &&
      this.context.isCurrent(owner) &&
      this.context.epoch() === owner.epoch
    );
  }
  private async restore() {
    const owner = this.context.scope();
    const target = this.saleId;
    const generation = ++this.generation;
    this.owner = owner;
    this.target = target;
    this.ready.set(false);
    this.preview.set(null);
    this.record.set(null);
    this.error.set('');
    this.reason = '';
    this.goodsConfirmed = this.cashConfirmed = false;
    if (!owner || !this.canManage()) return;
    try {
      const record = await this.drafts.load(owner, target);
      if (!this.current(owner, target, generation)) return;
      this.record.set(record);
      if (record) {
        this.reason = record.draft.payload.reason;
        this.goodsConfirmed = record.draft.payload.goodsRecoveredConfirmed;
        this.cashConfirmed = record.draft.payload.cashRefundConfirmed;
      }
      // An unresolved operation must never be replaced by a fresh preview or a fresh key.
      if (record?.pending) {
        await this.drafts.markUnknown(owner, target, record.pending.operationKey);
        if (!this.current(owner, target, generation)) return;
        this.record.set(await this.drafts.load(owner, target));
        this.ready.set(true);
        return;
      }
      if (record?.confirmedOperationKey) {
        this.error.set('sales.cancellation.confirmed');
        return;
      }
      const preview = await firstValueFrom(this.api.preview(target));
      if (!this.current(owner, target, generation)) return;
      this.preview.set(preview);
      this.ready.set(true);
    } catch {
      if (this.current(owner, target, generation))
        this.error.set('sales.cancellation.storageError');
    }
  }
  valid() {
    const p = this.preview();
    return (
      this.ready() &&
      !this.busy() &&
      !this.record()?.pending &&
      !this.record()?.confirmedOperationKey &&
      this.canManage() &&
      !!p?.eligible &&
      p.postingEnabled &&
      this.reason.trim().length >= 3 &&
      this.reason.trim().length <= 255 &&
      this.goodsConfirmed &&
      (!p.requiresCashRefundConfirmation || this.cashConfirmed)
    );
  }
  save() {
    if (!this.owner || !this.ready() || this.record()?.pending || !this.preview()) return;
    const owner = this.owner,
      target = this.target,
      generation = this.generation;
    const draft = this.editable();
    this.saves = this.saves.then(async () => {
      try {
        const result = await this.drafts.save(owner, target, draft, this.record()?.revision ?? 0);
        if (this.current(owner, target, generation)) this.record.set(result);
      } catch {
        if (this.current(owner, target, generation)) {
          this.error.set('sales.cancellation.storageError');
          this.ready.set(false);
        }
      }
    });
  }
  private editable(requireTill = false) {
    const { till: previousTill, ...previous } = this.record()?.draft.payload ?? {};
    const till = requireTill ? this.tillFields.context.posting()?.till : previousTill;
    return {
      payload: {
        ...previous,
        ...(till ? { till } : {}),
        expectedVersion: this.preview()!.expectedVersion,
        reason: this.reason.trim(),
        goodsRecoveredConfirmed: this.goodsConfirmed,
        cashRefundConfirmed: this.preview()!.requiresCashRefundConfirmation && this.cashConfirmed,
      },
    };
  }
  async submit() {
    if (!this.valid() || !this.owner) return;
    const owner = this.owner,
      target = this.target,
      generation = this.generation;
    this.busy.set(true);
    this.error.set('');
    try {
      await this.saves;
      const record = await this.drafts.prepare(
        owner,
        target,
        this.editable(true),
        this.record()?.revision ?? 0,
      );
      if (!this.current(owner, target, generation)) return;
      this.record.set(record);
      await this.send(owner, target, generation, record, record.pending!.status === 'unknown');
    } catch {
      if (this.current(owner, target, generation))
        this.error.set('sales.cancellation.storageError');
    } finally {
      if (this.current(owner, target, generation)) this.busy.set(false);
    }
  }
  async checkStatus() {
    await this.recover(false);
  }
  async retry() {
    await this.recover(true);
  }
  private async recover(retry: boolean) {
    const pending = this.record()?.pending;
    if (!pending || !this.owner || this.busy() || !this.canManage()) return;
    const owner = this.owner,
      target = this.target,
      generation = this.generation;
    this.busy.set(true);
    this.error.set('');
    try {
      if (await this.drafts.isConfirmed(owner, target, pending.operationKey)) {
        if (this.current(owner, target, generation)) this.error.set('sales.cancellation.confirmed');
        return;
      }
      const claimed = await this.drafts.claimRecovery(
        owner,
        target,
        pending.operationKey,
        pending.payload,
      );
      if (!this.current(owner, target, generation)) return;
      this.record.set(claimed);
      if (retry) {
        await this.send(owner, target, generation, claimed, true);
        return;
      }
      try {
        const result = await firstValueFrom(this.api.getOperation(pending.operationKey, owner));
        await this.confirm(owner, target, generation, pending.operationKey, result.response);
      } catch (error) {
        const settled = await this.drafts.settleFailure(
          owner,
          target,
          pending.operationKey,
          error,
          true,
        );
        if (this.current(owner, target, generation)) {
          this.record.set(settled.record);
          this.error.set('sales.cancellation.unknown');
        }
      }
    } catch {
      if (this.current(owner, target, generation))
        this.error.set('sales.cancellation.storageError');
    } finally {
      if (this.current(owner, target, generation)) this.busy.set(false);
    }
  }
  private async send(
    owner: ShopScope,
    target: string,
    generation: number,
    record: SaleCancellationDraftRecord,
    recovery: boolean,
  ) {
    const pending = record.pending!;
    if (!this.current(owner, target, generation)) {
      await this.drafts.markUnknown(owner, target, pending.operationKey);
      return;
    }
    const mutation = this.context.beginMutation();
    try {
      const result = await firstValueFrom(this.api.cancel(target, pending.payload, owner));
      await this.confirm(owner, target, generation, pending.operationKey, result);
    } catch (error) {
      const settled = await this.drafts.settleFailure(
        owner,
        target,
        pending.operationKey,
        error,
        recovery,
      );
      if (this.current(owner, target, generation)) {
        this.record.set(settled.record);
        this.error.set(
          settled.outcome === 'unknown'
            ? 'sales.cancellation.unknown'
            : 'sales.cancellation.rejected',
        );
      }
    } finally {
      mutation.release();
    }
  }
  private async confirm(
    owner: ShopScope,
    target: string,
    generation: number,
    key: string,
    result: SaleCancellationDto,
  ) {
    await this.drafts.settle(owner, target, key, 'confirmed');
    await this.drafts.clearConfirmed(owner, target, key);
    if (!this.current(owner, target, generation)) return;
    this.record.set(null);
    this.completed.emit(result);
    this.close();
  }
  close() {
    this.visible = false;
    this.generation++;
    this.visibleChange.emit(false);
  }
}
