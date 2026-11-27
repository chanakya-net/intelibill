import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { formatLocalIsoDate } from '../../../shared/utils/date-time.util';
import { HsnLookupResult } from './inventory.models';
import { InventoryService } from './inventory.service';

@Injectable({ providedIn: 'root' })
export class InventoryBatchDefaultsService {
  private readonly inventoryService = inject(InventoryService);

  generateBatchNumber(date = new Date()): string {
    const localDate = formatLocalIsoDate(date).replace(/-/g, '');
    const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    let suffix = '';
    for (let index = 0; index < 5; index += 1) {
      suffix += chars[Math.floor(Math.random() * chars.length)];
    }

    return `BN-${localDate}-${suffix}`;
  }

  lookupHsn(productName: string): Promise<HsnLookupResult> {
    return firstValueFrom(this.inventoryService.lookupHsn(productName));
  }

  parseTaxPercentage(taxPercentage: string): number {
    return Number.parseFloat(taxPercentage.replace('%', '').trim());
  }

  getAutoTaxRatePercent(result: HsnLookupResult): number | null {
    if (result.taxScenarios.length !== 1) {
      return null;
    }

    const taxRatePercent = this.parseTaxPercentage(result.taxScenarios[0].taxPercentage);
    return Number.isFinite(taxRatePercent) ? taxRatePercent : null;
  }
}
