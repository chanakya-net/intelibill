import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';

import { AuthService } from '../../../core/auth/auth.service';
import { ShopDetails, ShopService } from '../../shops/services/shop.service';
import { PurchaseOrderDetail, PurchaseOrderService } from '../services/purchase-order.service';

@Component({
  selector: 'app-purchase-order-print-view',
  standalone: true,
  imports: [CommonModule, TranslocoPipe],
  templateUrl: './purchase-order-print-view.component.html',
  styleUrl: './purchase-order-print-view.component.scss',
})
export class PurchaseOrderPrintViewComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly purchaseOrderService = inject(PurchaseOrderService);
  private readonly shopService = inject(ShopService);
  private readonly authService = inject(AuthService);

  readonly isLoading = signal(false);
  readonly errorMessage = signal('');
  readonly order = signal<PurchaseOrderDetail | null>(null);
  readonly shop = signal<ShopDetails | null>(null);

  constructor() {
    this.loadPurchaseOrder();
  }

  onPrintAgain(): void {
    window.print();
  }

  onClose(): void {
    window.close();
  }

  private loadPurchaseOrder(): void {
    const purchaseOrderId = this.route.snapshot.paramMap.get('purchaseOrderId');
    const activeShopId = this.authService.session()?.activeShopId;

    if (!purchaseOrderId) {
      this.errorMessage.set('Purchase order was not found.');
      return;
    }

    this.isLoading.set(true);
    this.errorMessage.set('');

    forkJoin({
      order: this.purchaseOrderService.getPurchaseOrderDetail(purchaseOrderId),
      shop: activeShopId
        ? this.shopService.getShopDetails(activeShopId).pipe(catchError(() => of(null)))
        : of(null),
    }).subscribe({
      next: ({ order, shop }) => {
        this.order.set(order);
        this.shop.set(shop);
        this.isLoading.set(false);
        setTimeout(() => window.print());
      },
      error: (error) => {
        this.errorMessage.set(error.error?.detail || 'Unable to load purchase order.');
        this.isLoading.set(false);
      },
    });
  }
}
