import { Component, Input, OnChanges, OnDestroy, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { Subscription, forkJoin } from 'rxjs';
import { ExpiryPolicyService, type ExpiryReadiness } from '../services/expiry-policy.service';
import type { ExpiryPolicySettings, ExpiryPolicyState } from '../services/expiry.models';
@Component({
  selector: 'app-expiry-policy-form',
  standalone: true,
  imports: [FormsModule, TranslocoPipe],
  templateUrl: './expiry-policy-form.component.html',
})
export class ExpiryPolicyFormComponent implements OnChanges, OnDestroy {
  readonly service = inject(ExpiryPolicyService);
  @Input() shopId = '';
  @Input() itemId?: string;
  @Input() canEdit = false;
  readonly state = signal<ExpiryPolicyState | null>(null);
  readonly readiness = signal<ExpiryReadiness | null>(null);
  readonly busy = signal(false);
  readonly message = signal('');
  zone = '';
  inherit = true;
  reason = '';
  policy: ExpiryPolicySettings = {
    mode: 'Ignore',
    expiryRequired: false,
    sellThroughExpiryDate: true,
    warningDays: 7,
    overrideAllowed: false,
  };
  private subscription?: Subscription;
  private generation = 0;
  private loadedScope = '';
  ngOnChanges(): void {
    this.load();
  }
  ngOnDestroy(): void {
    this.generation++;
    this.subscription?.unsubscribe();
  }
  load(): void {
    this.subscription?.unsubscribe();
    const generation = ++this.generation;
    this.state.set(null);
    this.readiness.set(null);
    this.reason = '';
    this.message.set('');
    if (!this.shopId || this.shopId !== this.service.activeShopId()) {
      this.message.set('expiry.switchShop');
      return;
    }
    this.loadedScope = this.service.scopeIdentity();
    this.busy.set(true);
    this.subscription = forkJoin({
      state: this.service.read(this.itemId),
      zone: this.service.businessTimeZone(),
    }).subscribe({
      next: ({ state, zone }) => {
        if (generation !== this.generation) return;
        this.apply(state);
        this.zone = zone;
        this.busy.set(false);
      },
      error: () => {
        if (generation === this.generation) {
          this.message.set('expiry.loadFailed');
          this.busy.set(false);
        }
      },
    });
    if (!this.itemId && ['owner', 'manager'].includes(this.service.role()))
      this.service.readiness().subscribe({
        next: (x) => {
          if (generation === this.generation) this.readiness.set(x);
        },
        error: () => {},
      });
  }
  save(): void {
    const current = this.state();
    const reason = this.reason.trim();
    if (
      this.busy() ||
      !current ||
      !this.canEdit ||
      this.service.role() !== 'owner' ||
      this.shopId !== this.service.activeShopId() ||
      this.loadedScope !== this.service.scopeIdentity()
    )
      return;
    if (!reason || reason.length > 500) {
      this.message.set('expiry.reasonRequired');
      return;
    }
    if (
      !Number.isInteger(this.policy.warningDays) ||
      this.policy.warningDays < 0 ||
      this.policy.warningDays > 365
    ) {
      this.message.set('expiry.invalidWindow');
      return;
    }
    const generation = this.generation;
    this.busy.set(true);
    this.message.set('');
    this.service
      .update(
        {
          expectedCatalogVersion: current.catalogVersion,
          policy: this.itemId && this.inherit ? null : { ...this.policy },
          reason,
        },
        this.itemId,
      )
      .subscribe({
        next: (state) => {
          if (generation !== this.generation) return;
          this.apply(state);
          this.busy.set(false);
          this.message.set('expiry.saved');
        },
        error: () => {
          if (generation !== this.generation) return;
          this.busy.set(false);
          this.message.set('expiry.saveFailed');
        },
      });
  }
  private apply(state: ExpiryPolicyState): void {
    this.state.set(state);
    const { version: _serverVersion, ...settings } = state.effectivePolicy;
    this.policy = settings;
    this.inherit = !state.overridePolicy;
    this.reason = '';
  }
}
