import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';

import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { UserShop } from '../../../core/auth/auth.models';
import { ShopService } from '../../shops/services/shop.service';

@Component({
  selector: 'app-set-default-store-overlay',
  standalone: true,
  imports: [CommonModule, ButtonModule, ProgressSpinnerModule],
  templateUrl: './set-default-store-overlay.component.html',
  styleUrl: './set-default-store-overlay.component.scss',
})
export class SetDefaultStoreOverlayComponent {
  private readonly shopService = inject(ShopService);

  readonly isSubmitting = signal(false);
  readonly serverError = signal('');

  @Input({ required: true }) shops: readonly UserShop[] = [];
  @Input() activeShopId: string | null = null;
  @Output() readonly closeRequested = new EventEmitter<void>();

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

    this.serverError.set('');
    this.isSubmitting.set(true);

    this.shopService.setDefaultShop(shopId).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.closeRequested.emit();
      },
      error: () => {
        this.serverError.set('Unable to set default store right now. Please try again.');
        this.isSubmitting.set(false);
      },
    });
  }
}
