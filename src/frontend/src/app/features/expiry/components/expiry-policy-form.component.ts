import { Component, Input, OnChanges, OnDestroy, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { InputNumberModule } from 'primeng/inputnumber';
import { SelectModule } from 'primeng/select';
import { TextareaModule } from 'primeng/textarea';
import { Subscription, forkJoin } from 'rxjs';
import { ExpiryPolicyService, type ExpiryReadiness } from '../services/expiry-policy.service';
import type { ExpiryPolicySettings, ExpiryPolicyState } from '../services/expiry.models';
@Component({
  selector: 'app-expiry-policy-form',
  standalone: true,
  imports: [
    FormsModule,
    TranslocoPipe,
    ButtonModule,
    CheckboxModule,
    InputNumberModule,
    SelectModule,
    TextareaModule,
  ],
  templateUrl: './expiry-policy-form.component.html',
  styleUrl: './expiry-policy-form.component.scss',
})
export class ExpiryPolicyFormComponent implements OnChanges, OnDestroy {
  readonly service = inject(ExpiryPolicyService);
  @Input() shopId = '';
  @Input() itemId?: string;
  @Input() canEdit = false;
  readonly state = signal<ExpiryPolicyState | null>(null);
  readonly readiness = signal<ExpiryReadiness | null>(null);
  readonly readinessBusy = signal(false);
  readonly readinessError = signal('');
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
  private readinessGeneration = 0;
  private readinessSubscription?: Subscription;
  private loadedScope = '';
  ngOnChanges(): void {
    this.load();
  }
  ngOnDestroy(): void {
    this.generation++;
    this.subscription?.unsubscribe();
    this.clearReadiness();
  }
  load(): void {
    this.subscription?.unsubscribe();
    const generation = ++this.generation;
    this.state.set(null);
    this.clearReadiness();
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
        if (!this.isCurrent(generation)) return;
        this.apply(state);
        this.zone = zone;
        this.refreshReadiness();
        this.busy.set(false);
      },
      error: () => {
        if (this.isCurrent(generation)) {
          this.message.set('expiry.loadFailed');
          this.busy.set(false);
        }
      },
    });
  }
  private isCurrent(generation: number): boolean {
    return (
      generation === this.generation &&
      this.shopId === this.service.activeShopId() &&
      this.loadedScope === this.service.scopeIdentity()
    );
  }
  private clearReadiness(): void {
    this.readinessGeneration++;
    this.readinessSubscription?.unsubscribe();
    this.readiness.set(null);
    this.readinessBusy.set(false);
    this.readinessError.set('');
  }
  refreshReadiness(): void {
    this.clearReadiness();
    const state = this.state();
    if (!state || this.itemId || !['owner', 'manager'].includes(this.service.role())) return;
    const generation = this.generation;
    const request = this.readinessGeneration;
    if (!this.isCurrent(generation)) return;
    this.readinessBusy.set(true);
    this.readinessSubscription = this.service.readiness().subscribe({
      next: (ready) => {
        if (!this.isCurrent(generation) || request !== this.readinessGeneration) return;
        this.readinessBusy.set(false);
        if (ready.catalogVersion !== this.state()?.catalogVersion) {
          this.readinessError.set('expiry.readinessFailed');
          return;
        }
        this.readiness.set(ready);
        this.readinessError.set('');
      },
      error: () => {
        if (!this.isCurrent(generation) || request !== this.readinessGeneration) return;
        this.readinessBusy.set(false);
        this.readinessError.set('expiry.readinessFailed');
      },
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
    this.clearReadiness();
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
          if (!this.isCurrent(generation)) return;
          this.apply(state);
          this.refreshReadiness();
          this.busy.set(false);
          this.message.set('expiry.saved');
        },
        error: () => {
          if (!this.isCurrent(generation)) return;
          this.busy.set(false);
          this.message.set('expiry.saveFailed');
          if (!this.itemId) this.readinessError.set('expiry.readinessFailed');
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
