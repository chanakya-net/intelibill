import { disabledTillProviders } from '../../../../tests/tills/disabled-till.providers';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { InvoiceLeaseIndexedDbService } from '../../../core/storage/invoice-lease-indexeddb.service';
import { OfflineSalesSnapshotIndexedDbService } from '../../../core/storage/offline-sales-snapshot-indexeddb.service';
import {
  OfflineFinalizeRequest,
  OfflineSaleFinalizationService,
} from './offline-sale-finalization.service';
import { mapOfflineQueuedSaleToSaleDto } from './offline-sale-invoice.mapper';
import type { InvoiceSellerProfile } from './sale.models';
import { OfflineSalesQueueIndexedDbService } from './offline-sales-queue-indexeddb.service';
import { OfflineSalesShadowStockIndexedDbService } from './offline-sales-shadow-stock-indexeddb.service';

describe('OfflineSaleFinalizationService', () => {
  const sellerA: InvoiceSellerProfile = {
    seller: {
      legalName: 'Main Shop Pvt Ltd',
      tradeName: 'Main Shop',
      address: '42 MG Road, Bengaluru, Karnataka 560001',
      stateCode: '29',
      stateName: 'Karnataka',
      gstin: '29AAPFU0939F1ZR',
      taxRegime: 'Regular',
    },
    eInvoiceStatus: 'BelowThreshold',
    seriesCode: 'A',
  };

  function setup(
    userId: string | null = 'user-1',
    sellerProfile: InvoiceSellerProfile | null = sellerA,
  ) {
    const snapshotDb = {
      getUsableSnapshotInfo: vi
        .fn()
        .mockResolvedValue({ snapshotId: 'snap-1', completedAt: new Date().toISOString(), schemaVersion: 4, businessTimeZoneId: 'Asia/Kolkata', catalogVersion: 3 }),
      getUsableBatches: vi.fn().mockResolvedValue([makeBatchSnapshot()]),
      getUsableServices: vi.fn().mockResolvedValue([]),
      getUsableDiscountRules: vi.fn().mockResolvedValue([]),
      getUsableCustomers: vi.fn().mockResolvedValue([{ customerId: 'cust-1' }]),
    };
    const leaseDb = {
      consumeNextInvoiceNumber: vi.fn().mockResolvedValue({
        invoiceNumber: 'A/2627/000001',
        lease: { nextNumber: 2, fiscalYear: '2026-27', sellerProfile },
      }),
      rollbackConsumedInvoiceNumber: vi.fn().mockResolvedValue({ nextNumber: 1 }),
    };
    const queueDb = {
      savePendingSale: vi.fn().mockResolvedValue(undefined),
    };
    const shadowDb = {
      ensureQuantity: vi.fn().mockResolvedValue(10),
      reduceQuantity: vi.fn().mockResolvedValue(9),
    };

    TestBed.configureTestingModule({
      providers: [
        ...disabledTillProviders(),
        OfflineSaleFinalizationService,
        {
          provide: AuthService,
          useValue: { session: () => (userId ? { user: { id: userId }, activeShopId: 'shop-1' } : null) },
        },
        { provide: OfflineSalesSnapshotIndexedDbService, useValue: snapshotDb },
        { provide: InvoiceLeaseIndexedDbService, useValue: leaseDb },
        { provide: OfflineSalesQueueIndexedDbService, useValue: queueDb },
        { provide: OfflineSalesShadowStockIndexedDbService, useValue: shadowDb },
      ],
    });

    return {
      service: TestBed.inject(OfflineSaleFinalizationService),
      snapshotDb,
      leaseDb,
      queueDb,
      shadowDb,
    };
  }

  it('refuses a legacy catalog for a new invoice before consuming its number', async () => {
    const { service, leaseDb, queueDb, shadowDb, snapshotDb } = setup();
    snapshotDb.getUsableSnapshotInfo.mockResolvedValue({ snapshotId: 'snap-1', completedAt: new Date().toISOString() } as never);
    expect(await service.finalizeAndQueue(makeRequest())).toEqual({ ok: false, reason: 'EXPIRY_REFRESH_REQUIRED' });
    expect(leaseDb.consumeNextInvoiceNumber).not.toHaveBeenCalled();
    expect(queueDb.savePendingSale).not.toHaveBeenCalled();
    expect(shadowDb.reduceQuantity).not.toHaveBeenCalled();
  });

  it('freezes exact issue-date evidence through sync and recovery without current decoration', async () => {
    const { service } = setup();
    const result = await service.finalizeAndQueue(makeRequest());
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    const proof = result.payload.pricing.lines[0].expiryEvidence;
    expect(proof).toMatchObject({ evidenceId: 'proof-1', catalogVersion: 3, policyVersion: 2, expiryDate: null, decisionCode: 'Allowed' });
    expect(proof?.businessDate).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });

  it('refuses offline warning approval even for the issuing manager', async () => {
    const { service, snapshotDb, leaseDb, queueDb } = setup();
    snapshotDb.getUsableBatches.mockResolvedValue([{ ...makeBatchSnapshot(), expiryDate: '2020-01-01',
      effectivePolicy: { version: 2, mode: 'Warn', expiryRequired: false, sellThroughExpiryDate: true, warningDays: 7, overrideAllowed: true } }]);
    expect(await service.finalizeAndQueue(makeRequest())).toEqual({ ok: false, reason: 'EXPIRY_ONLINE_APPROVAL_REQUIRED' });
    expect(leaseDb.consumeNextInvoiceNumber).not.toHaveBeenCalled();
    expect(queueDb.savePendingSale).not.toHaveBeenCalled();
  });

  it('caps an older shadow balance at the captured unquarantined availability', async () => {
    const { service, snapshotDb, shadowDb, leaseDb } = setup();
    snapshotDb.getUsableBatches.mockResolvedValue([{ ...makeBatchSnapshot(), quarantinedQuantity: 3, availableQuantity: 7 }]);
    shadowDb.ensureQuantity.mockResolvedValue(10);
    expect(await service.finalizeAndQueue(withQuantities(8))).toEqual({ ok: false, reason: 'INSUFFICIENT_SHADOW_STOCK' });
    expect(leaseDb.consumeNextInvoiceNumber).not.toHaveBeenCalled();
  });

  it('seeds shadow stock from unquarantined availability rather than physical quantity', async () => {
    const { service, snapshotDb, shadowDb } = setup();
    snapshotDb.getUsableBatches.mockResolvedValue([{ ...makeBatchSnapshot(), quarantinedQuantity: 3, availableQuantity: 7 }]);
    expect((await service.finalizeAndQueue(makeRequest())).ok).toBe(true);
    expect(shadowDb.ensureQuantity).toHaveBeenCalledWith('shop-1', 'device-1', 'batch-1', 7);
  });

  it('refuses below recorded cost using snapshot cost before consuming or saving anything', async () => {
    const { service, snapshotDb, leaseDb, queueDb, shadowDb } = setup();
    snapshotDb.getUsableBatches.mockResolvedValue([
      {
        ...makeBatchSnapshot(),
        costPrice: 100,
        salesPrice: 118,
        taxRatePercent: 18,
        taxIncluded: true,
      },
    ]);
    const base = makeRequest();
    const result = await service.finalizeAndQueue({
      ...base,
      pricingInput: {
        ...base.pricingInput,
        lines: [
          { ...base.pricingInput.lines[0], costPrice: 1, itemDiscount: { type: 2, value: 10 } },
        ],
      },
    });
    expect(result).toEqual({ ok: false, reason: 'BELOW_RECORDED_COST' });
    expect(leaseDb.consumeNextInvoiceNumber).not.toHaveBeenCalled();
    expect(queueDb.savePendingSale).not.toHaveBeenCalled();
    expect(shadowDb.reduceQuantity).not.toHaveBeenCalled();
  });

  it.each([
    [0.99, true],
    [1, false],
  ] as const)(
    'issues the midpoint-safe discount boundary and refuses the next cent (%s)',
    async (discount, accepted) => {
      const { service, snapshotDb, leaseDb } = setup();
      snapshotDb.getUsableBatches.mockResolvedValue([
        {
          ...makeBatchSnapshot(),
          salesPrice: 4,
          mrp: 4,
          costPrice: 2.01,
          taxRatePercent: 0,
          taxIncluded: false,
          quantityScale: 3,
        },
      ]);
      const base = makeRequest();
      const result = await service.finalizeAndQueue({
        ...base,
        pricingInput: {
          ...base.pricingInput,
          lines: [
            {
              ...base.pricingInput.lines[0],
              quantity: 0.5,
              itemDiscount: { type: 2, value: discount },
            },
          ],
        },
      });
      expect(result.ok).toBe(accepted);
      if (!accepted) {
        expect(result).toEqual({ ok: false, reason: 'BELOW_RECORDED_COST' });
        expect(leaseDb.consumeNextInvoiceNumber).not.toHaveBeenCalled();
      }
    },
  );

  it('refuses new frozen till evidence from another owner/shop before issuing or queueing', async () => {
    const { service, queueDb, leaseDb } = setup('user-1');
    vi.spyOn(TestBed.inject(AuthService), 'session').mockReturnValue({
      user: { id: 'user-1' },
      activeShopId: 'another-shop',
    } as ReturnType<AuthService['session']>);
    const result = await service.finalizeAndQueue({
      ...makeRequest(),
      tillEvidence: {
        till: { sessionId: 'drawer' },
        tenderLines: [{ method: 'Cash', appliedAmount: 100 }],
        tillBusinessDate: '2026-11-08',
        tillCashierId: 'other',
      },
    });
    expect(result).toEqual({ ok: false, reason: 'TILL_CONTEXT_INVALID' });
    expect(queueDb.savePendingSale).not.toHaveBeenCalled();
    expect(leaseDb.consumeNextInvoiceNumber).not.toHaveBeenCalled();
  });

  it('queues sale, consumes invoice, and reduces shadow stock', async () => {
    vi.spyOn(crypto, 'randomUUID')
      .mockReturnValueOnce('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa')
      .mockReturnValueOnce('11111111-1111-4111-8111-111111111111');
    const { service, leaseDb, queueDb, shadowDb } = setup();

    const result = await service.finalizeAndQueue(makeRequest());

    expect(result.ok).toBe(true);
    expect(leaseDb.consumeNextInvoiceNumber).toHaveBeenCalledWith('shop-1', 'device-1', '2026-27');
    expect(queueDb.savePendingSale).toHaveBeenCalledTimes(1);
    expect(leaseDb.rollbackConsumedInvoiceNumber).not.toHaveBeenCalled();
    expect(shadowDb.reduceQuantity).toHaveBeenCalledWith('shop-1', 'device-1', 'batch-1', 1);
  });

  it('stamps the signed-in user as issuer', async () => {
    const { service, queueDb } = setup('user-7');

    const result = await service.finalizeAndQueue(makeRequest());

    expect(result.ok && result.payload.issuedByUserId).toBe('user-7');
    expect(queueDb.savePendingSale.mock.calls[0][0].payload.issuedByUserId).toBe('user-7');
  });

  it('refuses new issuance when no user id is known', async () => {
    const { service } = setup(null);

    const result = await service.finalizeAndQueue(makeRequest());

    expect(result).toEqual({ ok: false, reason: 'EXPIRY_REFRESH_REQUIRED' });
  });

  it('freezes pricing and catalog values from cached batch snapshots', async () => {
    vi.spyOn(crypto, 'randomUUID')
      .mockReturnValueOnce('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa')
      .mockReturnValueOnce('11111111-1111-4111-8111-111111111111');
    const { service, queueDb } = setup();
    const baseRequest = makeRequest();
    const request: OfflineFinalizeRequest = {
      ...baseRequest,
      pricingInput: {
        ...baseRequest.pricingInput,
        lines: [
          {
            ...baseRequest.pricingInput.lines[0],
            clientLineId: 'client-line-1',
            itemId: 'caller-item',
            barcode: 'caller-barcode',
            itemName: 'Caller item',
            batchNumber: 'CALLER-BATCH',
            salesPrice: 1,
            mrp: 2,
            costPrice: 3,
            taxRatePercent: 99,
            taxIncluded: true,
            hsnCode: 'caller-hsn',
          },
        ],
      },
    };

    const result = await service.finalizeAndQueue(request);

    expect(result.ok).toBe(true);
    const saved = queueDb.savePendingSale.mock.calls[0][0];
    expect(saved.payload.pricing.calculationVersion).toBe(1);
    expect(result.ok && result.payload).toBe(saved.payload);
    expect(saved.payload.pricing.lines[0]).toMatchObject({
      clientLineId: 'client-line-1',
      inventoryBatchId: 'batch-1',
      itemId: 'item-1',
      barcode: '111',
      itemName: 'A',
      batchNumber: 'B1',
      quantity: 1,
      salesPrice: 100,
      mrp: 100,
      costPrice: 80,
      taxRatePercent: 0,
      taxIncluded: false,
      hsnCode: null,
      preTaxAmount: 100,
      lineTotal: 100,
    });
  });

  it('finalization_copies_seller_identity_once', async () => {
    const profile = structuredClone(sellerA);
    const { service, queueDb, snapshotDb } = setup('user-1', profile);
    snapshotDb.getUsableCustomers.mockResolvedValue([
      {
        customerId: 'cust-1',
        name: 'Buyer Pvt Ltd',
        phoneNumber: '9876543210',
        gstin: null,
        stateCode: '29',
        address: '4 Market St',
      },
    ]);

    const result = await service.finalizeAndQueue(makeRequest());

    expect(result.ok).toBe(true);
    const saved = queueDb.savePendingSale.mock.calls[0][0].payload;
    expect(saved.document).toMatchObject({
      version: 2,
      number: 'A/2627/000001',
      seriesCode: 'A',
      fiscalYearStart: 2026,
      issuedAt: saved.soldAt,
      seller: sellerA.seller,
      buyer: { name: 'Buyer Pvt Ltd', address: '4 Market St', stateCode: '29' },
      totals: { invoiceTotal: 100, totalTax: 0 },
    });

    // The shop changes its GSTIN and address after the sale: the issued copy does not follow.
    const mutable = profile.seller as { gstin: string | null; address: string };
    mutable.gstin = '27AAPFU0939F1ZV';
    mutable.address = '1 New Road, Pune';

    const reprinted = mapOfflineQueuedSaleToSaleDto(saved);
    expect(reprinted.document?.seller).toEqual(sellerA.seller);
    expect(reprinted.document?.number).toBe('A/2627/000001');
    expect(reprinted.invoiceNumber).toBe('A/2627/000001');
    expect(reprinted.totalAmount).toBe(100);
  });

  it('refuses a customer the offline snapshot does not hold, before consuming a number', async () => {
    const { service, leaseDb, queueDb } = setup();
    const request = makeRequest();

    const result = await service.finalizeAndQueue({
      ...request,
      pricingInput: { ...request.pricingInput, customerId: 'cust-created-after-snapshot' },
    });

    // The server would issue to the customer's live record (perhaps with a GSTIN) while the paper showed a walk-in.
    expect(result).toEqual({ ok: false, reason: 'CUSTOMER_UNAVAILABLE' });
    expect(leaseDb.consumeNextInvoiceNumber).not.toHaveBeenCalled();
    expect(queueDb.savePendingSale).not.toHaveBeenCalled();
  });

  it('refuses_without_lease_seller_profile', async () => {
    const { service, leaseDb, queueDb, shadowDb } = setup('user-1', null);

    const result = await service.finalizeAndQueue(makeRequest());

    expect(result).toEqual({ ok: false, reason: 'SELLER_PROFILE_UNAVAILABLE' });
    expect(leaseDb.rollbackConsumedInvoiceNumber).toHaveBeenCalledWith(
      'shop-1',
      'device-1',
      '2026-27',
      2,
    );
    expect(queueDb.savePendingSale).not.toHaveBeenCalled();
    expect(shadowDb.reduceQuantity).not.toHaveBeenCalled();
  });

  it('refuses_document_errors_and_rolls_back', async () => {
    const composition: InvoiceSellerProfile = {
      ...sellerA,
      seller: { ...sellerA.seller, taxRegime: 'Composition' },
    };
    const { service, leaseDb, queueDb, snapshotDb } = setup('user-1', composition);
    snapshotDb.getUsableBatches.mockResolvedValue([{ ...makeBatchSnapshot(), taxRatePercent: 18 }]);

    const result = await service.finalizeAndQueue(makeRequest());

    expect(result).toEqual({
      ok: false,
      reason: 'DOCUMENT_UNAVAILABLE',
      errorCode: 'Invoice.CompositionCannotCollectTax',
    });
    expect(leaseDb.rollbackConsumedInvoiceNumber).toHaveBeenCalledWith(
      'shop-1',
      'device-1',
      '2026-27',
      2,
    );
    expect(queueDb.savePendingSale).not.toHaveBeenCalled();
  });

  it('blocks stale snapshots older than 48h', async () => {
    const { service, snapshotDb } = setup();
    snapshotDb.getUsableSnapshotInfo.mockResolvedValue({
      snapshotId: 'snap-1',
      completedAt: new Date(Date.now() - 49 * 60 * 60 * 1000).toISOString(),
    });

    const result = await service.finalizeAndQueue(makeRequest());
    expect(result).toEqual({ ok: false, reason: 'SNAPSHOT_STALE' });
  });

  it('blocks missing catalog item', async () => {
    const { service, snapshotDb } = setup();
    snapshotDb.getUsableBatches.mockResolvedValue([]);

    const result = await service.finalizeAndQueue(makeRequest());
    expect(result).toEqual({ ok: false, reason: 'MISSING_CATALOG_ITEM' });
  });

  it('blocks insufficient local shadow stock', async () => {
    const { service, shadowDb } = setup();
    shadowDb.ensureQuantity.mockResolvedValue(0);

    const result = await service.finalizeAndQueue(makeRequest());
    expect(result).toEqual({ ok: false, reason: 'INSUFFICIENT_SHADOW_STOCK' });
  });

  it('blocks when combined quantity for same batch exceeds shadow stock', async () => {
    const { service, shadowDb } = setup();
    shadowDb.ensureQuantity.mockResolvedValue(1);

    const baseRequest = makeRequest();
    const request: OfflineFinalizeRequest = {
      ...baseRequest,
      pricingInput: {
        ...baseRequest.pricingInput,
        lines: [
          baseRequest.pricingInput.lines[0],
          {
            ...baseRequest.pricingInput.lines[0],
            quantity: 1,
          },
        ],
      },
    };

    const result = await service.finalizeAndQueue(request);
    expect(result).toEqual({ ok: false, reason: 'INSUFFICIENT_SHADOW_STOCK' });
    expect(shadowDb.ensureQuantity).toHaveBeenCalledTimes(1);
  });

  it('sums 0.1 and 0.2 of one batch as exactly 0.3 of shadow stock', async () => {
    const { service, snapshotDb, shadowDb } = setup();
    snapshotDb.getUsableBatches.mockResolvedValue([
      { ...makeBatchSnapshot(), quantityScale: 3, documentUnit: 'KGS' },
    ]);
    shadowDb.ensureQuantity.mockResolvedValue(0.3);

    const result = await service.finalizeAndQueue(withQuantities(0.1, 0.2));

    expect(result.ok).toBe(true);
    expect(shadowDb.ensureQuantity).toHaveBeenCalledTimes(1);
    expect(shadowDb.reduceQuantity).toHaveBeenNthCalledWith(
      1,
      'shop-1',
      'device-1',
      'batch-1',
      0.1,
    );
    expect(shadowDb.reduceQuantity).toHaveBeenNthCalledWith(
      2,
      'shop-1',
      'device-1',
      'batch-1',
      0.2,
    );
  });

  it('freezes the snapshot document unit on each goods line', async () => {
    const { service, snapshotDb, queueDb } = setup();
    snapshotDb.getUsableBatches.mockResolvedValue([
      { ...makeBatchSnapshot(), quantityScale: 3, documentUnit: 'KGS' },
    ]);

    const result = await service.finalizeAndQueue(withQuantities(0.25));

    expect(result.ok).toBe(true);
    const saved = queueDb.savePendingSale.mock.calls[0][0];
    expect(saved.payload.pricing.lines[0]).toMatchObject({ quantity: 0.25, documentUnit: 'KGS' });
    expect(saved.payload.document.lines[0]).toMatchObject({ uom: 'KGS', quantity: 0.25 });
  });

  it('prints NOS for a batch from a snapshot taken before units', async () => {
    const { service, queueDb } = setup();

    const result = await service.finalizeAndQueue(makeRequest());

    expect(result.ok).toBe(true);
    expect(queueDb.savePendingSale.mock.calls[0][0].payload.pricing.lines[0].documentUnit).toBe(
      'NOS',
    );
  });

  it('refuses a part quantity of a batch from a snapshot taken before units, before consuming a number', async () => {
    const { service, leaseDb, queueDb, shadowDb } = setup();

    const result = await service.finalizeAndQueue(withQuantities(0.5));

    expect(result).toEqual({ ok: false, reason: 'QUANTITY_PRECISION_EXCEEDED' });
    expect(leaseDb.consumeNextInvoiceNumber).not.toHaveBeenCalled();
    expect(queueDb.savePendingSale).not.toHaveBeenCalled();
    expect(shadowDb.reduceQuantity).not.toHaveBeenCalled();
  });

  it('refuses more decimals than the snapshot unit allows', async () => {
    const { service, snapshotDb } = setup();
    snapshotDb.getUsableBatches.mockResolvedValue([
      { ...makeBatchSnapshot(), quantityScale: 3, documentUnit: 'KGS' },
    ]);

    await expect(service.finalizeAndQueue(withQuantities(0.0001))).resolves.toEqual({
      ok: false,
      reason: 'QUANTITY_PRECISION_EXCEEDED',
    });
  });

  it('requires cached customer when due amount exists', async () => {
    vi.spyOn(crypto, 'randomUUID').mockReturnValueOnce('11111111-1111-4111-8111-111111111111');
    const { service, snapshotDb } = setup();
    snapshotDb.getUsableCustomers.mockResolvedValue([]);

    const baseRequest = makeRequest();
    const request: OfflineFinalizeRequest = {
      ...baseRequest,
      pricingInput: {
        ...baseRequest.pricingInput,
        paidAmount: 0,
        customerId: 'missing',
      },
    };

    const result = await service.finalizeAndQueue(request);
    expect(result).toEqual({ ok: false, reason: 'MISSING_DUE_CUSTOMER' });
  });

  it('blocks a non-finite amount before consuming an invoice number', async () => {
    const { service, leaseDb, queueDb } = setup();
    const baseRequest = makeRequest();
    const request: OfflineFinalizeRequest = {
      ...baseRequest,
      pricingInput: {
        ...baseRequest.pricingInput,
        lines: [
          { ...baseRequest.pricingInput.lines[0], itemDiscount: { type: 2, value: Number.NaN } },
        ],
      },
    };

    const result = await service.finalizeAndQueue(request);
    expect(result).toEqual({ ok: false, reason: 'INVALID_AMOUNT' });
    expect(leaseDb.consumeNextInvoiceNumber).not.toHaveBeenCalled();
    expect(queueDb.savePendingSale).not.toHaveBeenCalled();
  });

  it('blocks when invoice is unavailable', async () => {
    const { service, leaseDb } = setup();
    leaseDb.consumeNextInvoiceNumber.mockRejectedValue(new Error('no lease'));

    const result = await service.finalizeAndQueue(makeRequest());
    expect(result).toEqual({ ok: false, reason: 'INVOICE_UNAVAILABLE' });
  });

  it('restores consumed invoice when queue persistence fails', async () => {
    vi.spyOn(crypto, 'randomUUID').mockReturnValueOnce('11111111-1111-4111-8111-111111111111');
    const { service, leaseDb, queueDb, shadowDb } = setup();
    const persistenceError = new Error('queue write failed');
    queueDb.savePendingSale.mockRejectedValue(persistenceError);

    await expect(service.finalizeAndQueue(makeRequest())).rejects.toBe(persistenceError);

    expect(leaseDb.consumeNextInvoiceNumber).toHaveBeenCalledWith('shop-1', 'device-1', '2026-27');
    expect(leaseDb.rollbackConsumedInvoiceNumber).toHaveBeenCalledWith(
      'shop-1',
      'device-1',
      '2026-27',
      2,
    );
    expect(shadowDb.reduceQuantity).not.toHaveBeenCalled();
  });

  function makeRequest(): OfflineFinalizeRequest {
    return {
      shopId: 'shop-1',
      deviceId: 'device-1',
      fiscalYear: '2026-27',
      maxSnapshotAgeMs: 48 * 60 * 60 * 1000,
      pricingInput: {
        soldAt: new Date().toISOString(),
        paymentMethod: 1,
        paidAmount: 100,
        customerId: 'cust-1' as string | null,
        customerName: 'A' as string | null,
        customerPhone: '9999999999' as string | null,
        saleDiscount: { type: 0 as const, value: 0 },
        rules: [],
        lines: [
          {
            inventoryBatchId: 'batch-1',
            itemId: 'item-1',
            barcode: '111',
            itemName: 'A',
            batchNumber: 'B1',
            quantity: 1,
            salesPrice: 100,
            mrp: 100,
            costPrice: 80,
            taxRatePercent: 0,
            taxIncluded: false,
            itemDiscount: { type: 0 as const, value: 0 },
            hsnCode: null as string | null,
          },
        ],
      },
    };
  }

  /** One line per quantity, all of batch-1, paid in full. */
  function withQuantities(...quantities: number[]): OfflineFinalizeRequest {
    const baseRequest = makeRequest();
    return {
      ...baseRequest,
      pricingInput: {
        ...baseRequest.pricingInput,
        paidAmount: 1_000,
        lines: quantities.map((quantity, index) => ({
          ...baseRequest.pricingInput.lines[0],
          clientLineId: `line-${index + 1}`,
          quantity,
        })),
      },
    };
  }

  function makeBatchSnapshot() {
    return {
      expiryEvidenceId: 'proof-1',
      effectivePolicy: { version: 2, mode: 'Ignore' as const, expiryRequired: false, sellThroughExpiryDate: true, warningDays: 7, overrideAllowed: false },
      physicalQuantity: 10, quarantinedQuantity: 0, availableQuantity: 10, batchVersion: 8,
      expiryCapturedAt: new Date(Date.now() - 1000).toISOString(), expiryEvidenceExpiresAt: new Date(Date.now() + 86400000).toISOString(),
      batchId: 'batch-1',
      itemId: 'item-1',
      barcode: '111',
      itemName: 'A',
      batchNumber: 'B1',
      quantity: 10,
      salesPrice: 100,
      mrp: 100,
      costPrice: 80,
      taxRatePercent: 0,
      taxIncluded: false,
      hsnCode: null,
    };
  }
});
