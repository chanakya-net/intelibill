import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { forkJoin } from 'rxjs';

import { AuthService } from '../../../../core/auth/auth.service';
import { ShopDetails, ShopService } from '../../../shops/services/shop.service';
import type { CreditNotePrintDto } from '../../services/sale.models';
import { SaleService } from '../../services/sale.service';

@Component({
  selector: 'app-credit-note-print-page',
  standalone: true,
  imports: [CommonModule, TranslocoPipe],
  templateUrl: './credit-note-print-page.component.html',
  styleUrl: './credit-note-print-page.component.scss',
})
export class CreditNotePrintPageComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly saleService = inject(SaleService);
  private readonly shopService = inject(ShopService);
  private readonly authService = inject(AuthService);

  readonly isLoading = signal(false);
  readonly errorMessage = signal('');
  readonly creditNote = signal<CreditNotePrintDto | null>(null);
  readonly shop = signal<ShopDetails | null>(null);

  constructor() {
    this.loadCreditNote();
  }

  onPrintAgain(): void {
    window.print();
  }

  onClose(): void {
    window.close();
  }

  isExpired(creditNote: CreditNotePrintDto): boolean {
    return creditNote.status === 'Expired';
  }

  isVoided(creditNote: CreditNotePrintDto): boolean {
    return creditNote.status === 'Voided';
  }

  statusKey(creditNote: CreditNotePrintDto): string {
    return `sales.creditNotes.print.status.${creditNote.status}`;
  }

  private loadCreditNote(): void {
    const code = this.route.snapshot.paramMap.get('code');
    const activeShopId = this.authService.session()?.activeShopId;

    if (!code) {
      this.errorMessage.set('Credit note was not found.');
      return;
    }

    if (!activeShopId) {
      this.errorMessage.set('Active shop was not found.');
      return;
    }

    this.isLoading.set(true);
    this.errorMessage.set('');

    forkJoin({
      creditNote: this.saleService.getCreditNotePrintByCode(code),
      shop: this.shopService.getShopDetails(activeShopId),
    }).subscribe({
      next: ({ creditNote, shop }) => {
        this.creditNote.set(creditNote);
        this.shop.set(shop);
        this.isLoading.set(false);
        setTimeout(() => window.print());
      },
      error: (error) => {
        this.errorMessage.set(error.error?.detail || 'Unable to load printable credit note.');
        this.isLoading.set(false);
      },
    });
  }
}
