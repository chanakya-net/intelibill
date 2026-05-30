import { NewSalePageCartSubmitService } from './new-sale-page.cart-submit.service';
import { SalePreviewLineDto } from '../services/sale.models';

export abstract class NewSalePageDiscountActionsService extends NewSalePageCartSubmitService {
  createSaleIdempotencyKey(): string {
    if (globalThis.crypto?.randomUUID) {
      return `sale-${globalThis.crypto.randomUUID()}`;
    }

    return `sale-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
  }

  onCancel(): void {
    this.router.navigate(['/sales']);
  }

  toggleSaleDiscountEditor(): void {
    this.isSaleDiscountEditorOpen.update((v) => !v);
  }

  onSaleDiscountTypeChange(type: 0 | 1 | 2): void {
    this.saleDiscountError.set('');
    const preview = this.checkoutPreview();
    if (preview) {
      const limits = this.getSaleDiscountLimits();
      if (!limits.isEligible || (type === 1 && limits.maxPercent <= 0) || (type === 2 && limits.maxFlat <= 0)) {
        this.saleDiscountType.set(0);
        this.saleDiscountValue.set(0);
        return;
      }
    }

    this.saleDiscountType.set(type);
    if (type === 0) {
      this.saleDiscountValue.set(0);
      return;
    }

    // Re-validate current value under new type.
    this.onSaleDiscountValueChange(this.saleDiscountValue());
  }

  onSaleDiscountValueChange(value: number | null | undefined): void {
    const normalized = this.roundAmount(Math.max(0, Number(value ?? 0)));
    const type = this.saleDiscountType();

    if (type === 0) {
      this.saleDiscountValue.set(0);
      this.saleDiscountError.set('');
      return;
    }

    const preview = this.checkoutPreview();
    if (!preview) {
      if (type === 1 && normalized > 100) {
        this.saleDiscountError.set('sales.newSale.discounts.exceedsMaxPercent');
        return;
      }
      this.saleDiscountError.set('');
      this.saleDiscountValue.set(normalized);
      return;
    }

    const limits = this.getSaleDiscountLimits();
    if (!limits.isEligible) {
      this.saleDiscountError.set('sales.newSale.discounts.saleNotEligible');
      this.saleDiscountValue.set(0);
      return;
    }

    const maxAllowed = type === 1 ? limits.maxPercent : limits.maxFlat;
    if (normalized > maxAllowed) {
      this.saleDiscountError.set(
        type === 1 ? 'sales.newSale.discounts.exceedsMaxPercent' : 'sales.newSale.discounts.exceedsMaxFlat'
      );
      return; // block invalid input
    }

    this.saleDiscountError.set('');
    this.saleDiscountValue.set(normalized);
  }

  isSaleDiscountEligible(): boolean {
    return this.getSaleDiscountLimits().isEligible;
  }

  getPreviewLine(clientLineKey: string): SalePreviewLineDto | null {
    const preview = this.checkoutPreview();
    if (!preview || !clientLineKey) return null;
    return preview.lines.find((l) => l.clientLineKey === clientLineKey) ?? null;
  }

  toggleLineDiscountEditor(clientLineKey: string): void {
    if (!clientLineKey) return;
    if (!this.cart().some((line) => line.clientLineKey === clientLineKey)) {
      return;
    }
    this.openLineDiscountEditorByKey.update((current) => ({
      ...current,
      [clientLineKey]: !current[clientLineKey],
    }));
  }

  isLineDiscountEditorOpen(clientLineKey: string): boolean {
    return Boolean(this.openLineDiscountEditorByKey()[clientLineKey]);
  }

  getCartItemDiscountError(clientLineKey: string): string {
    return this.cartItemDiscountErrorByKey()[clientLineKey] ?? '';
  }

  getCartItemHsnError(clientLineKey: string): string {
    return this.cartItemHsnErrorByKey()[clientLineKey] ?? '';
  }

  getCartItemTaxError(clientLineKey: string): string {
    return this.cartItemTaxErrorByKey()[clientLineKey] ?? '';
  }

  onCartItemDiscountTypeChange(clientLineKey: string, type: 0 | 1 | 2): void {
    if (!clientLineKey) return;

    this.cartItemDiscountErrorByKey.update((current) => ({ ...current, [clientLineKey]: '' }));

    const limits = this.getLineDiscountLimits(clientLineKey);
    const isAllowed =
      type === 0 ||
      (type === 1 && limits.maxPercent > 0) ||
      (type === 2 && limits.maxFlat > 0) ||
      !Number.isFinite(type);

    const nextType = isAllowed ? type : 0;
    this.cartState.setItemDiscountType(clientLineKey, nextType);

    if (nextType === 0) {
      return;
    }

    const current = this.cart().find((x) => x.clientLineKey === clientLineKey);
    this.onCartItemDiscountValueChange(clientLineKey, current?.itemDiscountValue ?? 0);
  }

  onCartItemDiscountValueChange(clientLineKey: string, value: number | null | undefined): void {
    if (!clientLineKey) return;
    const normalized = this.roundAmount(Math.max(0, Number(value ?? 0)));

    const item = this.cart().find((x) => x.clientLineKey === clientLineKey);
    if (!item) return;

    if (item.itemDiscountType === 0) {
      this.cartItemDiscountErrorByKey.update((current) => ({ ...current, [clientLineKey]: '' }));
      this.cartState.setItemDiscountValue(clientLineKey, 0);
      return;
    }

    const limits = this.getLineDiscountLimits(clientLineKey);
    const maxAllowed = item.itemDiscountType === 1 ? limits.maxPercent : limits.maxFlat;
    if (Number.isFinite(maxAllowed) && normalized > maxAllowed) {
      this.cartItemDiscountErrorByKey.update((current) => ({
        ...current,
        [clientLineKey]:
          item.itemDiscountType === 1
            ? 'sales.newSale.discounts.exceedsMaxPercent'
            : 'sales.newSale.discounts.exceedsMaxFlat',
      }));
      return; // block invalid input
    }

    this.cartItemDiscountErrorByKey.update((current) => ({ ...current, [clientLineKey]: '' }));
    this.cartState.setItemDiscountValue(clientLineKey, normalized);
  }

  onCartItemHsnCodeChange(clientLineKey: string, value: string | null | undefined): void {
    if (!clientLineKey) return;
    this.cartState.setItemHsnCode(clientLineKey, this.normalizeHsnCode(value));
  }

  onCartItemTaxRateChange(clientLineKey: string, value: number | null | undefined): void {
    if (!clientLineKey) return;
    const raw = Number(value ?? 0);
    const normalized = Number.isFinite(raw) ? this.roundAmount(raw) : 0;
    this.cartState.setItemTaxRatePercent(clientLineKey, normalized);
  }

  getLineDiscountLimits(clientLineKey: string): { maxFlat: number; maxPercent: number } {
    const preview = this.checkoutPreview();
    const line = preview?.lines.find((l) => l.clientLineKey === clientLineKey) ?? null;
    if (!line) {
      return { maxFlat: Number.POSITIVE_INFINITY, maxPercent: 100 };
    }

    const baseMaxFlat = Math.max(0, Number(line.maxAllowedItemDiscountFlat ?? 0));
    const baseMaxPercent = Math.max(0, Number(line.maxAllowedItemDiscountPercent ?? 0));

    if (line.configuredBatchRulePercentage == null) {
      return { maxFlat: baseMaxFlat, maxPercent: baseMaxPercent };
    }

    const configuredPercent = Math.max(0, Number(line.configuredBatchRulePercentage));
    const configuredAmount = this.roundAmount((Number(line.preTaxAmountBeforeDiscount) * configuredPercent) / 100);

    return {
      maxFlat: Math.min(baseMaxFlat, configuredAmount),
      maxPercent: Math.min(baseMaxPercent, configuredPercent),
    };
  }

}
