import { NewSalePageDiscountActionsService } from './new-sale-page.discount-actions.service';
import { CartItem } from '../services/sale-cart-state.service';

export abstract class NewSalePageDiscountValidationService extends NewSalePageDiscountActionsService {
  revalidateDiscountsAgainstPreview(): void {
    this.revalidateSaleDiscountAgainstPreview();
    this.revalidateLineDiscountsAgainstPreview();
  }

  revalidateSaleDiscountAgainstPreview(): void {
    const type = this.saleDiscountType();
    if (type === 0) {
      this.saleDiscountValue.set(0);
      this.saleDiscountError.set('');
      return;
    }

    const limits = this.getSaleDiscountLimits();
    const maxAllowed = type === 1 ? limits.maxPercent : limits.maxFlat;
    const isAllowed = limits.isEligible && maxAllowed > 0;
    if (!isAllowed) {
      this.saleDiscountType.set(0);
      this.saleDiscountValue.set(0);
      this.saleDiscountError.set('');
      return;
    }

    const normalized = this.roundAmount(Math.max(0, Number(this.saleDiscountValue() ?? 0)));
    if (normalized > maxAllowed) {
      // Clamp down to the new max so stale higher values cannot be submitted.
      this.saleDiscountValue.set(this.roundAmount(maxAllowed));
    }
    this.saleDiscountError.set('');
  }

  revalidateLineDiscountsAgainstPreview(): void {
    const cart = this.cart();
    if (cart.length === 0) {
      this.cartItemDiscountErrorByKey.set({});
      return;
    }

    const updatesByKey: Record<
      string,
      { nextType: 0 | 1 | 2; nextValue: number; nextError: string } | undefined
    > = {};

    for (const item of cart) {
      const key = item.clientLineKey;
      if (!key) continue;

      const currentType = (item.itemDiscountType ?? 0) as 0 | 1 | 2;
      if (currentType === 0) {
        updatesByKey[key] = { nextType: 0, nextValue: 0, nextError: '' };
        continue;
      }

      const limits = this.getLineDiscountLimits(key);
      const maxAllowed = currentType === 1 ? limits.maxPercent : limits.maxFlat;
      const isAllowed = Number.isFinite(maxAllowed) ? maxAllowed > 0 : true;

      if (!isAllowed) {
        updatesByKey[key] = { nextType: 0, nextValue: 0, nextError: '' };
        continue;
      }

      const normalized = this.roundAmount(Math.max(0, Number(item.itemDiscountValue ?? 0)));
      if (Number.isFinite(maxAllowed) && normalized > maxAllowed) {
        updatesByKey[key] = {
          nextType: currentType,
          nextValue: this.roundAmount(maxAllowed),
          nextError: '',
        };
        continue;
      }

      updatesByKey[key] = { nextType: currentType, nextValue: normalized, nextError: '' };
    }

    let needsCartUpdate = false;
    let needsErrorUpdate = false;

    for (const item of cart) {
      const key = item.clientLineKey;
      const update = updatesByKey[key];
      if (!update) continue;
      if (item.itemDiscountType !== update.nextType || item.itemDiscountValue !== update.nextValue) {
        needsCartUpdate = true;
      }
      if ((this.cartItemDiscountErrorByKey()[key] ?? '') !== update.nextError) {
        needsErrorUpdate = true;
      }
    }

    if (needsErrorUpdate) {
      this.cartItemDiscountErrorByKey.update((current) => {
        const next = { ...current };
        for (const [key, update] of Object.entries(updatesByKey)) {
          if (!update) continue;
          next[key] = update.nextError;
        }
        return next;
      });
    }

    if (needsCartUpdate) {
      this.cartState.applyDiscountAdjustments(
        Object.entries(updatesByKey)
          .filter((entry): entry is [string, { nextType: 0 | 1 | 2; nextValue: number; nextError: string }] => !!entry[1])
          .map(([clientLineKey, update]) => ({
            clientLineKey,
            nextType: update.nextType,
            nextValue: update.nextValue,
          }))
      );
    }
  }

  revalidateLineOverrides(cart: readonly CartItem[]): boolean {
    if (cart.length === 0) {
      this.cartItemHsnErrorByKey.set({});
      this.cartItemTaxErrorByKey.set({});
      return false;
    }

    const hsnErrors: Record<string, string> = {};
    const taxErrors: Record<string, string> = {};

    for (const item of cart) {
      const key = item.clientLineKey;
      if (!key) continue;

      const normalizedHsn = this.normalizeHsnCode(item.hsnCode);
      if (!this.isValidHsnCode(normalizedHsn)) {
        hsnErrors[key] = 'sales.newSale.hsnInvalid';
      }

      if (!this.isValidTaxRatePercent(item.taxRatePercent)) {
        taxErrors[key] = 'sales.newSale.taxRateInvalid';
      }
    }

    this.cartItemHsnErrorByKey.set(hsnErrors);
    this.cartItemTaxErrorByKey.set(taxErrors);
    return Object.keys(hsnErrors).length > 0 || Object.keys(taxErrors).length > 0;
  }

  getBlockingCartValidationErrorKey(cart: readonly CartItem[]): string | null {
    const hasOverrideErrors = this.revalidateLineOverrides(cart);
    if (hasOverrideErrors) {
      return 'sales.newSale.overrides.fixErrors';
    }

    if (this.saleDiscountError() || Object.values(this.cartItemDiscountErrorByKey()).some((v) => !!v)) {
      return 'sales.newSale.discounts.fixErrors';
    }

    return null;
  }

  isValidHsnCode(value: string | null): boolean {
    if (!value) return true;
    return /^\d{4,8}$/.test(value);
  }

  isValidTaxRatePercent(value: number): boolean {
    if (!Number.isFinite(value)) {
      return false;
    }
    if (value < 0 || value > 100) {
      return false;
    }
    return Math.abs(value - this.roundAmount(value)) < 0.0001;
  }

  normalizeHsnCode(value: string | null | undefined): string | null {
    const trimmed = (value ?? '').trim();
    return trimmed.length > 0 ? trimmed : null;
  }

  getSaleDiscountLimits(): { isEligible: boolean; maxFlat: number; maxPercent: number } {
    const preview = this.checkoutPreview();
    if (!preview) {
      return { isEligible: false, maxFlat: 0, maxPercent: 0 };
    }

    const eligibleSubtotal = Math.max(0, Number(preview.saleLevelEligibleSubtotal ?? 0));
    if (eligibleSubtotal <= 0) {
      return { isEligible: false, maxFlat: 0, maxPercent: 0 };
    }

    const totalCapacity = preview.lines.reduce((sum, line) => {
      const preTax = Number(line.preTaxAmountBeforeDiscount ?? 0);
      const itemDiscount = Number(line.itemDiscountAmount ?? 0);
      const taxableAfterItem = preTax - itemDiscount;
      const costTotal = Number(line.costPrice ?? 0) * Number(line.quantity ?? 0);
      return sum + Math.max(0, taxableAfterItem - costTotal);
    }, 0);

    let maxFlat = this.roundAmount(Math.max(0, totalCapacity));
    let maxPercent = maxFlat > 0 ? this.roundAmount((maxFlat * 100) / eligibleSubtotal) : 0;

    const configured = preview.configuredSaleRule;
    if (configured && Number.isFinite(Number(configured.percentage))) {
      const configuredPercent = Math.max(0, Number(configured.percentage));
      const configuredAmount = this.roundAmount((eligibleSubtotal * configuredPercent) / 100);
      maxFlat = Math.min(maxFlat, configuredAmount);
      maxPercent = Math.min(maxPercent, configuredPercent);
    }

    return { isEligible: maxFlat > 0, maxFlat, maxPercent };
  }

}
