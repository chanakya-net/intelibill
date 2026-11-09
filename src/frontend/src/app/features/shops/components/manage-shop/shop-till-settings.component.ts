import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { TextareaModule } from 'primeng/textarea';
import { Subscription } from 'rxjs';
import {
  ShopTillSettingsService,
  type ShopTillSettings,
  type UpdateShopTillSettings,
} from '../../services/shop-till-settings.service';

@Component({
  selector: 'app-shop-till-settings',
  standalone: true,
  imports: [FormsModule, RouterLink, TranslocoPipe, ButtonModule, CheckboxModule, TextareaModule],
  templateUrl: './shop-till-settings.component.html',
  styleUrl: './shop-till-settings.component.scss',
})
export class ShopTillSettingsComponent {
  readonly api = inject(ShopTillSettingsService);
  readonly shopId = input.required<string>();
  readonly canEdit = input(false);
  readonly disabled = input(false);
  readonly navigateRequested = output<void>();
  readonly state = signal<ShopTillSettings | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly editing = signal(false);
  readonly uncertain = signal(false);
  readonly error = signal('');
  readonly saved = signal(false);
  readonly isActive = computed(() => this.shopId() === this.api.activeShopId());
  readonly owner = computed(() => this.canEdit() && this.api.role() === 'owner');
  readonly blocked = computed(() => {
    const s = this.state();
    return (
      !!s?.enabled &&
      (s.openSessionCount > 0 || s.awaitingReviewCount > 0 || s.pendingOfflineCount > 0)
    );
  });
  reason = '';
  confirmed = false;
  private generation = 0;
  private readSubscription?: Subscription;
  private writeSubscription?: Subscription;
  private attempted: UpdateShopTillSettings | null = null;
  private loadedScope = '';
  constructor() {
    effect(() => {
      const shopId = this.shopId();
      const scope = this.api.scopeIdentity();
      untracked(() => {
        this.generation++;
        this.writeSubscription?.unsubscribe();
        this.readSubscription?.unsubscribe();
        this.state.set(null);
        this.saving.set(false);
        this.editing.set(false);
        this.uncertain.set(false);
        this.attempted = null;
        this.loadedScope = scope;
        this.reason = '';
        this.confirmed = false;
        this.load(shopId, scope);
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.generation++;
      this.readSubscription?.unsubscribe();
      this.writeSubscription?.unsubscribe();
    });
  }
  refresh() {
    if (!this.saving()) this.load(this.shopId(), this.api.scopeIdentity());
  }
  private current(generation: number, shopId: string, scope: string) {
    return (
      generation === this.generation &&
      shopId === this.shopId() &&
      shopId === this.api.activeShopId() &&
      scope === this.api.scopeIdentity()
    );
  }
  private load(shopId: string, scope: string) {
    this.error.set('');
    this.saved.set(false);
    this.editing.set(false);
    if (
      !shopId ||
      shopId !== this.api.activeShopId() ||
      !['owner', 'manager'].includes(this.api.role())
    ) {
      this.loading.set(false);
      return;
    }
    this.readSubscription?.unsubscribe();
    this.loading.set(true);
    const generation = this.generation;
    this.readSubscription = this.api.read(shopId).subscribe({
      next: (state) => {
        if (!this.current(generation, shopId, scope)) return;
        this.state.set(state);
        this.loadedScope = scope;
        this.loading.set(false);
        if (this.attempted && state.version > this.attempted.expectedVersion) {
          this.uncertain.set(false);
          this.attempted = null;
        }
      },
      error: () => {
        if (this.current(generation, shopId, scope)) {
          this.loading.set(false);
          this.error.set('tills.settings.loadFailed');
        }
      },
    });
  }
  beginChange() {
    if (
      !this.owner() ||
      !this.isActive() ||
      this.disabled() ||
      this.loading() ||
      this.saving() ||
      this.uncertain() ||
      !this.state()?.available ||
      this.blocked() ||
      this.loadedScope !== this.api.scopeIdentity()
    )
      return;
    this.reason = '';
    this.confirmed = false;
    this.saved.set(false);
    this.error.set('');
    this.editing.set(true);
  }
  cancel() {
    if (!this.saving()) this.editing.set(false);
  }
  save() {
    const state = this.state();
    if (
      !state ||
      !this.editing() ||
      !this.owner() ||
      !this.isActive() ||
      this.disabled() ||
      this.loading() ||
      this.saving() ||
      this.uncertain() ||
      !state.available ||
      this.blocked() ||
      !this.confirmed ||
      !this.reason.trim() ||
      this.reason.trim().length > 500 ||
      this.loadedScope !== this.api.scopeIdentity()
    )
      return;
    this.attempted = {
      enabled: !state.enabled,
      expectedVersion: state.version,
      reason: this.reason.trim(),
      confirmed: true,
    };
    this.send(this.attempted);
  }
  retry() {
    if (
      this.attempted &&
      this.uncertain() &&
      this.owner() &&
      this.isActive() &&
      !this.disabled() &&
      !this.saving() &&
      !this.loading() &&
      this.loadedScope === this.api.scopeIdentity()
    )
      this.send(this.attempted);
  }
  private send(request: UpdateShopTillSettings) {
    const shopId = this.shopId();
    const scope = this.api.scopeIdentity();
    const generation = this.generation;
    this.saving.set(true);
    this.error.set('');
    this.saved.set(false);
    this.writeSubscription = this.api.update(shopId, request).subscribe({
      next: (state) => {
        if (!this.current(generation, shopId, scope)) return;
        this.state.set(state);
        this.saving.set(false);
        this.editing.set(false);
        this.uncertain.set(false);
        this.attempted = null;
        this.saved.set(true);
        this.reason = '';
        this.confirmed = false;
      },
      error: (error: unknown) => {
        if (!this.current(generation, shopId, scope)) return;
        this.saving.set(false);
        this.editing.set(false);
        if (!(error instanceof HttpErrorResponse) || error.status === 0 || error.status >= 500) {
          this.uncertain.set(true);
          return;
        }
        if (error.status === 409) {
          this.error.set('tills.settings.changed');
          this.uncertain.set(false);
          this.attempted = null;
          this.state.set(null);
          return;
        }
        this.uncertain.set(false);
        this.attempted = null;
        this.error.set('tills.settings.saveFailed');
      },
    });
  }
}
