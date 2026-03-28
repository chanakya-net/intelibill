import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnInit, Output, effect, inject, signal } from '@angular/core';
import { Store } from '@ngrx/store';

import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { UserShop } from '../../../core/auth/auth.models';
import { RootState } from '../../../core/state/app.state';
import { ShopsActions } from '../../shops/state/shops.actions';
import {
  selectShopsErrorMessage,
  selectShopsLastMutationSucceeded,
  selectShopsLastMutationType,
  selectShopsSubmitting,
} from '../../shops/state/shops.selectors';

@Component({
  selector: 'app-set-default-store-overlay',
  standalone: true,
  imports: [CommonModule, ButtonModule, ProgressSpinnerModule],
  templateUrl: './set-default-store-overlay.component.html',
  styleUrl: './set-default-store-overlay.component.scss',
})
export class SetDefaultStoreOverlayComponent implements OnInit {
  private readonly store = inject(Store<RootState>);

  readonly isSubmitting = this.store.selectSignal(selectShopsSubmitting);
  readonly serverError = this.store.selectSignal(selectShopsErrorMessage);
  readonly lastMutationType = this.store.selectSignal(selectShopsLastMutationType);
  readonly lastMutationSucceeded = this.store.selectSignal(selectShopsLastMutationSucceeded);
  readonly isSetDefaultPending = signal(false);

  @Input({ required: true }) shops: readonly UserShop[] = [];
  @Input() activeShopId: string | null = null;
  @Output() readonly closeRequested = new EventEmitter<void>();

  constructor() {
    effect(() => {
      const isSetDefaultSuccess = this.lastMutationType() === 'set-default' && this.lastMutationSucceeded();
      if (!this.isSetDefaultPending() || !isSetDefaultSuccess || this.isSubmitting()) {
        return;
      }

      this.isSetDefaultPending.set(false);
      this.store.dispatch(ShopsActions.clearMutationStatus());
      this.closeRequested.emit();
    });
  }

  ngOnInit(): void {
    this.store.dispatch(ShopsActions.clearError());
    this.store.dispatch(ShopsActions.clearMutationStatus());
  }

  onClose(): void {
    if (this.isSubmitting()) {
      return;
    }

    this.closeRequested.emit();
  }

  onSetDefault(shopId: string): void {
    if (this.isSubmitting()) {
      return;
    }

    if (shopId === this.activeShopId) {
      this.closeRequested.emit();
      return;
    }

    this.store.dispatch(ShopsActions.clearError());
    this.store.dispatch(ShopsActions.clearMutationStatus());
    this.isSetDefaultPending.set(true);
    this.store.dispatch(ShopsActions.setDefaultShopRequested({ shopId }));
  }
}
