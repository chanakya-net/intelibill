import { disabledTillProviders } from '../../../../tests/tills/disabled-till.providers';
import { TestBed } from '@angular/core/testing';
import { beforeAll, describe, expect, it, vi } from 'vitest';

import { ShopContextService } from '../../../core/context/shop-context.service';
import { OfflineIssuanceLock } from '../../../core/storage/offline-issuance-lock';
import { OfflinePermitClockIndexedDbService } from '../../../core/storage/offline-permit-clock-indexeddb.service';
import { OFFLINE_PERMIT_PUBLIC_KEYS } from '../utils/offline-permit-signature';
import {
  PERMIT_DEVICE_ID,
  PERMIT_GRANT_ID,
  PERMIT_GRANT_SHA256,
  PERMIT_KEY_ID,
  PERMIT_LEASE_ID,
  PERMIT_SESSION_ID,
  PERMIT_SHOP_ID,
  PERMIT_SNAPSHOT_ID,
  PERMIT_USER_ID,
  createPermitSigner,
  permitFields,
  storedPermit,
  withForgedSignature,
  type PermitFields,
  type PermitSigner,
} from '../../../../tests/sales/offline-permit-signing.fixture';

import { AuthService } from '../../../core/auth/auth.service';
import { InvoiceLeaseIndexedDbService } from '../../../core/storage/invoice-lease-indexeddb.service';
import { OfflineSalesSnapshotIndexedDbService } from '../../../core/storage/offline-sales-snapshot-indexeddb.service';
import {
  OfflineFinalizeRequest,
  OfflineSaleFinalizationService,
} from './offline-sale-finalization.service';
import { mapOfflineQueuedSaleToSaleDto } from './offline-sale-invoice.mapper';
import type { InvoiceSellerProfile } from './sale.models';
import { OfflineQueueWriteRefusedError, OfflineSalesQueueIndexedDbService } from './offline-sales-queue-indexeddb.service';
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

  let signer: PermitSigner;
  let signedFields: PermitFields;
  let signedPermit: string;

  beforeAll(async () => {
    signer = await createPermitSigner();
    signedFields = permitFields(Date.now());
    signedPermit = await signer.sign(signedFields);
  });

  /** The lease as the device stores it: anchored to its grant, carrying the delivered signed permit. */
  function makeLease(sellerProfile: InvoiceSellerProfile | null, fields = signedFields, token = signedPermit) {
    return {
      leaseId: PERMIT_LEASE_ID,
      shopId: PERMIT_SHOP_ID,
      deviceId: PERMIT_DEVICE_ID,
      fiscalYear: '2026-27',
      prefix: 'A/2627/',
      numberPadding: 6,
      rangeStart: 1,
      rangeEnd: 100,
      nextNumber: 1,
      remainingCount: 100,
      reservedAt: new Date(Date.now() - 120_000).toISOString(),
      expiresAt: new Date(Date.now() + 7 * 86_400_000).toISOString(),
      sellerProfile,
      licenceEvidence: { version: 1, grantId: PERMIT_GRANT_ID, grantSha256: PERMIT_GRANT_SHA256, keyId: PERMIT_KEY_ID },
      offlinePermit: storedPermit(fields, token),
    };
  }

  function setup(
    userId: string | null = PERMIT_USER_ID,
    sellerProfile: InvoiceSellerProfile | null = sellerA,
  ) {
    const lease = makeLease(sellerProfile);
    const snapshotDb = {
      getUsableSnapshotInfo: vi
        .fn()
        .mockResolvedValue({ snapshotId: PERMIT_SNAPSHOT_ID, completedAt: signedFields.snapshotCompletedAt, schemaVersion: 4, businessTimeZoneId: 'Asia/Kolkata', catalogVersion: 3 }),
      getUsableBatches: vi.fn().mockResolvedValue([makeBatchSnapshot()]),
      getUsableServices: vi.fn().mockResolvedValue([]),
      getUsableDiscountRules: vi.fn().mockResolvedValue([]),
      getUsableCustomers: vi.fn().mockResolvedValue([{ customerId: 'cust-1' }]),
    };
    // Stateful like the real store: the second guard re-reads the lease the number was taken from.
    let storedLease: ReturnType<typeof makeLease> = lease;
    const sessionState = { sessionId: PERMIT_SESSION_ID };
    const leaseDb = {
      loadLease: vi.fn(async (): Promise<unknown> => storedLease),
      consumeNextInvoiceNumber: vi.fn(async () => {
        storedLease = { ...storedLease, nextNumber: storedLease.nextNumber + 1, remainingCount: 99 };
        return { invoiceNumber: 'A/2627/000001', lease: storedLease, remainingCount: 99 };
      }),
      rollbackConsumedInvoiceNumber: vi.fn().mockResolvedValue({ nextNumber: 1 }),
      loadPendingClaim: vi.fn().mockResolvedValue(null),
      settleClaim: vi.fn().mockResolvedValue(undefined),
    };
    const queueDb = {
      savePendingSale: vi.fn().mockResolvedValue(undefined),
      getQueuedSale: vi.fn().mockResolvedValue(null),
    };
    const clock = { evaluate: vi.fn().mockResolvedValue('consistent') };
    const lockState = { available: true };
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
          useValue: {
            session: () =>
              userId ? { user: { id: userId }, activeShopId: PERMIT_SHOP_ID, sessionId: sessionState.sessionId } : null,
          },
        },
        { provide: OfflineSalesSnapshotIndexedDbService, useValue: snapshotDb },
        { provide: InvoiceLeaseIndexedDbService, useValue: leaseDb },
        { provide: OfflineSalesQueueIndexedDbService, useValue: queueDb },
        { provide: OfflineSalesShadowStockIndexedDbService, useValue: shadowDb },
        // Trust is the throwaway public half only; the build's own pins are an empty map.
        { provide: OFFLINE_PERMIT_PUBLIC_KEYS, useValue: { [signer.keyId]: signer.publicKeySpki } },
        { provide: OfflinePermitClockIndexedDbService, useValue: clock },
        {
          provide: OfflineIssuanceLock,
          useValue: {
            runExclusive: async (_shop: string, work: () => Promise<unknown>) =>
              lockState.available ? { acquired: true, value: await work() } : { acquired: false },
          },
        },
      ],
    });

    if (userId) TestBed.inject(ShopContextService).initialize({ userId, shopId: PERMIT_SHOP_ID });

    return {
      service: TestBed.inject(OfflineSaleFinalizationService),
      lease,
      clock,
      lockState,
      sessionState,
      setLease: (next: ReturnType<typeof makeLease>) => { storedLease = next; },
      snapshotDb,
      leaseDb,
      queueDb,
      shadowDb,
    };
  }

  it('issues a genuinely sellable cart under a validly signed stored permit (control)', async () => {
    const { service, leaseDb, queueDb, shadowDb } = setup();

    const result = await service.finalizeAndQueue(makeRequest());

    expect(result.ok).toBe(true);
    expect(leaseDb.consumeNextInvoiceNumber).toHaveBeenCalledTimes(1);
    expect(queueDb.savePendingSale).toHaveBeenCalledTimes(1);
    expect(shadowDb.reduceQuantity).toHaveBeenCalledTimes(1);
  });

  it('refuses a forged permit signature before consuming a number, queueing or reducing stock', async () => {
    const { service, leaseDb, queueDb, shadowDb, lease } = setup();
    // The only difference from the control above is the signature bytes of the stored token.
    const forged = withForgedSignature(signedPermit);
    leaseDb.loadLease.mockResolvedValue({ ...lease, offlinePermit: storedPermit(signedFields, forged) });

    const result = await service.finalizeAndQueue(makeRequest());

    expect({
      result,
      numbersConsumed: leaseDb.consumeNextInvoiceNumber.mock.calls.length,
      queueInserts: queueDb.savePendingSale.mock.calls.length,
      stockReductions: shadowDb.reduceQuantity.mock.calls.length,
    }).toEqual({
      result: { ok: false, reason: 'OFFLINE_PERMIT_INVALID' },
      numbersConsumed: 0,
      queueInserts: 0,
      stockReductions: 0,
    });
  });

  it('refuses a legacy catalog for a new invoice before consuming its number', async () => {
    const { service, leaseDb, queueDb, shadowDb, snapshotDb } = setup();
    snapshotDb.getUsableSnapshotInfo.mockResolvedValue({ snapshotId: PERMIT_SNAPSHOT_ID, completedAt: new Date().toISOString() } as never);
    expect(await service.finalizeAndQueue(makeRequest())).toEqual({ ok: false, reason: 'EXPIRY_REFRESH_REQUIRED' });
    expect(leaseDb.consumeNextInvoiceNumber).not.toHaveBeenCalled();
    expect(queueDb.savePendingSale).not.toHaveBeenCalled();
    expect(shadowDb.reduceQuantity).not.toHaveBeenCalled();
  });

  for (const [field, value] of [
    ['expiryDate', undefined], ['manufacturingDate', undefined],
    ['expiryDate', '2026-02-30'], ['manufacturingDate', 'not-a-date'],
    ['expiryDate', '2026-11-09T00:00:00Z'],
  ] as const) {
    it(`refuses new issuance with incomplete date authority ${field}=${value}`, async () => {
      const { service, snapshotDb, leaseDb, queueDb, shadowDb } = setup();
      const batch: Record<string, unknown> = { ...makeBatchSnapshot(), [field]: value };
      if (value === undefined) delete batch[field];
      snapshotDb.getUsableBatches.mockResolvedValue([batch] as never);
      expect(await service.finalizeAndQueue(makeRequest())).toEqual({ ok: false, reason: 'EXPIRY_REFRESH_REQUIRED' });
      expect(leaseDb.consumeNextInvoiceNumber).not.toHaveBeenCalled();
      expect(queueDb.savePendingSale).not.toHaveBeenCalled();
      expect(shadowDb.reduceQuantity).not.toHaveBeenCalled();
    });
  }

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
    expect(shadowDb.ensureQuantity).toHaveBeenCalledWith(PERMIT_SHOP_ID, 'device-1', 'batch-1', 7);
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
    const { service, queueDb, leaseDb } = setup(PERMIT_USER_ID);
    vi.spyOn(TestBed.inject(AuthService), 'session').mockReturnValue({
      user: { id: PERMIT_USER_ID },
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
    expect(leaseDb.consumeNextInvoiceNumber).toHaveBeenCalledWith(PERMIT_SHOP_ID, 'device-1', '2026-27', {
      leaseId: PERMIT_LEASE_ID,
      nextNumber: 1,
      signedPermit,
      clientSaleId: expect.any(String),
    });
    expect(queueDb.savePendingSale).toHaveBeenCalledTimes(1);
    expect(leaseDb.rollbackConsumedInvoiceNumber).not.toHaveBeenCalled();
    expect(shadowDb.reduceQuantity).toHaveBeenCalledWith(PERMIT_SHOP_ID, 'device-1', 'batch-1', 1);
  });

  it('stamps the signed-in user as issuer', async () => {
    const { service, queueDb } = setup();

    const result = await service.finalizeAndQueue(makeRequest());

    expect(result.ok && result.payload.issuedByUserId).toBe(PERMIT_USER_ID);
    expect(queueDb.savePendingSale.mock.calls[0][0].payload.issuedByUserId).toBe(PERMIT_USER_ID);
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
    const { service, queueDb, snapshotDb } = setup(PERMIT_USER_ID, profile);
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
    const { service, leaseDb, queueDb, shadowDb } = setup(PERMIT_USER_ID, null);

    const result = await service.finalizeAndQueue(makeRequest());

    expect(result).toEqual({ ok: false, reason: 'SELLER_PROFILE_UNAVAILABLE' });
    expect(leaseDb.rollbackConsumedInvoiceNumber).toHaveBeenCalledWith(
      PERMIT_SHOP_ID,
      'device-1',
      '2026-27',
      2,
      { leaseId: PERMIT_LEASE_ID, number: 1, clientSaleId: expect.any(String) },
    );
    expect(queueDb.savePendingSale).not.toHaveBeenCalled();
    expect(shadowDb.reduceQuantity).not.toHaveBeenCalled();
  });

  it('refuses_document_errors_and_rolls_back', async () => {
    const composition: InvoiceSellerProfile = {
      ...sellerA,
      seller: { ...sellerA.seller, taxRegime: 'Composition' },
    };
    const { service, leaseDb, queueDb, snapshotDb } = setup(PERMIT_USER_ID, composition);
    snapshotDb.getUsableBatches.mockResolvedValue([{ ...makeBatchSnapshot(), taxRatePercent: 18 }]);

    const result = await service.finalizeAndQueue(makeRequest());

    expect(result).toEqual({
      ok: false,
      reason: 'DOCUMENT_UNAVAILABLE',
      errorCode: 'Invoice.CompositionCannotCollectTax',
    });
    expect(leaseDb.rollbackConsumedInvoiceNumber).toHaveBeenCalledWith(
      PERMIT_SHOP_ID,
      'device-1',
      '2026-27',
      2,
      { leaseId: PERMIT_LEASE_ID, number: 1, clientSaleId: expect.any(String) },
    );
    expect(queueDb.savePendingSale).not.toHaveBeenCalled();
  });

  it('blocks stale snapshots older than 48h', async () => {
    const { service, snapshotDb } = setup();
    snapshotDb.getUsableSnapshotInfo.mockResolvedValue({
      snapshotId: PERMIT_SNAPSHOT_ID,
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
      PERMIT_SHOP_ID,
      'device-1',
      'batch-1',
      0.1,
    );
    expect(shadowDb.reduceQuantity).toHaveBeenNthCalledWith(
      2,
      PERMIT_SHOP_ID,
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

    expect(leaseDb.consumeNextInvoiceNumber).toHaveBeenCalledTimes(1);
    expect(leaseDb.rollbackConsumedInvoiceNumber).toHaveBeenCalledWith(
      PERMIT_SHOP_ID,
      'device-1',
      '2026-27',
      2,
      { leaseId: PERMIT_LEASE_ID, number: 1, clientSaleId: expect.any(String) },
    );
    expect(shadowDb.reduceQuantity).not.toHaveBeenCalled();
  });

  describe('offline permission', () => {
    const zeroWrites = (dbs: { leaseDb: ReturnType<typeof setup>['leaseDb']; queueDb: ReturnType<typeof setup>['queueDb']; shadowDb: ReturnType<typeof setup>['shadowDb'] }) => ({
      numbersConsumed: dbs.leaseDb.consumeNextInvoiceNumber.mock.calls.length,
      queueInserts: dbs.queueDb.savePendingSale.mock.calls.length,
      stockReductions: dbs.shadowDb.reduceQuantity.mock.calls.length,
    });
    const NONE = { numbersConsumed: 0, queueInserts: 0, stockReductions: 0 };

    async function leaseSigned(overrides: Partial<PermitFields>, by: PermitSigner = signer) {
      const fields = { ...signedFields, ...overrides };
      return { ...makeLease(sellerA, fields, await by.sign(fields)), offlinePermit: storedPermit(fields, await by.sign(fields), by.keyId) };
    }

    for (const [name, overrides] of [
      ['another shop', { shopId: '11111111-2222-4333-8444-555555555556' }],
      ['another user', { userId: '99999999-8888-4777-8666-555555555556' }],
      ['a replacement session of the same owner', { sessionId: 'abcdefab-cdef-4abc-8def-abcdefabcdee' }],
      ['another device', { deviceId: 'device-2' }],
      ['another lease', { leaseId: 'fedcba98-7654-4321-8fed-cba987654322' }],
      ['another grant', { grantId: '0a6f3c52-7d1e-4b8a-9c55-1f2e3d4c5b6b' }],
      ['another grant digest', { grantSha256: 'f'.repeat(64) }],
      ['an older snapshot than the one promoted', { snapshotId: '5a5a5a5a-1111-4222-8333-444444444445' }],
      ['a till session the sale is not frozen under', { tillSessionId: '7a7a7a7a-1111-4222-8333-444444444444' }],
    ] as const) {
      it(`refuses a genuinely signed permit bound to ${name}`, async () => {
        const ctx = setup();
        ctx.setLease(await leaseSigned(overrides));

        expect(await ctx.service.finalizeAndQueue(makeRequest())).toEqual({ ok: false, reason: 'OFFLINE_PERMIT_INVALID' });
        expect(zeroWrites(ctx)).toEqual(NONE);
      });
    }

    it('refuses a permit validly signed by a key this build does not pin', async () => {
      const ctx = setup();
      const stranger = await createPermitSigner('stranger-key-1');
      ctx.setLease(await leaseSigned({}, stranger));

      expect(await ctx.service.finalizeAndQueue(makeRequest())).toEqual({ ok: false, reason: 'OFFLINE_PERMIT_INVALID' });
      expect(zeroWrites(ctx)).toEqual(NONE);
    });

    it('refuses stored display fields that no longer repeat the signed bytes', async () => {
      const ctx = setup();
      const later = new Date(Date.now() + 30 * 86_400_000).toISOString().replace('Z', '000Z');
      ctx.setLease({ ...ctx.lease, offlinePermit: { ...ctx.lease.offlinePermit, validUntil: later } });

      expect(await ctx.service.finalizeAndQueue(makeRequest())).toEqual({ ok: false, reason: 'OFFLINE_PERMIT_INVALID' });
      expect(zeroWrites(ctx)).toEqual(NONE);
    });

    it('refuses a lease with no stored permit, and a legacy lease with no grant evidence', async () => {
      const ctx = setup();
      ctx.setLease({ ...ctx.lease, offlinePermit: null } as never);
      expect(await ctx.service.finalizeAndQueue(makeRequest())).toEqual({ ok: false, reason: 'OFFLINE_PERMIT_INVALID' });
      ctx.setLease({ ...ctx.lease, licenceEvidence: null } as never);
      expect(await ctx.service.finalizeAndQueue(makeRequest())).toEqual({ ok: false, reason: 'OFFLINE_PERMIT_INVALID' });
      expect(zeroWrites(ctx)).toEqual(NONE);
    });

    it('refuses a session without a session id before anything is read', async () => {
      const ctx = setup();
      ctx.sessionState.sessionId = '';
      expect(await ctx.service.finalizeAndQueue(makeRequest())).toEqual({ ok: false, reason: 'OFFLINE_PERMIT_INVALID' });
      expect(zeroWrites(ctx)).toEqual(NONE);
    });

    describe('signed window [notBefore, validUntil) at a fixed clock', () => {
      const NOW = Date.parse('2026-11-22T11:59:45.000Z');
      const at = (ms: number, micros = 0) => new Date(ms).toISOString().replace('Z', `${String(micros).padStart(3, '0')}Z`);

      async function windowed(notBefore: string, validUntil: string, soldAt = new Date(NOW).toISOString()) {
        vi.useFakeTimers({ toFake: ['Date'] });
        vi.setSystemTime(NOW);
        const ctx = setup();
        ctx.snapshotDb.getUsableSnapshotInfo.mockResolvedValue({ snapshotId: PERMIT_SNAPSHOT_ID, completedAt: notBefore, schemaVersion: 4, businessTimeZoneId: 'Asia/Kolkata', catalogVersion: 3 });
        ctx.snapshotDb.getUsableBatches.mockResolvedValue([{ ...makeBatchSnapshot(), expiryCapturedAt: at(NOW - 1000), expiryEvidenceExpiresAt: at(NOW + 86_400_000) }]);
        const lease = await leaseSigned({ snapshotCompletedAt: notBefore, issuedAt: notBefore, notBefore, validUntil });
        ctx.setLease({ ...lease, expiresAt: at(NOW + 86_400_000) });
        const request = makeRequest();
        try {
          const result = await ctx.service.finalizeAndQueue({ ...request, pricingInput: { ...request.pricingInput, soldAt } });
          return { result, writes: zeroWrites(ctx) };
        } finally {
          vi.useRealTimers();
        }
      }

      it('issues exactly at notBefore', async () => {
        const { result, writes } = await windowed(at(NOW), at(NOW + 60_000));
        expect(result.ok).toBe(true);
        expect(writes).toEqual({ numbersConsumed: 1, queueInserts: 1, stockReductions: 1 });
      });

      it('refuses one microsecond before notBefore', async () => {
        const { result, writes } = await windowed(at(NOW, 1), at(NOW + 60_000));
        expect(result).toEqual({ ok: false, reason: 'OFFLINE_PERMIT_EXPIRED', reconnectBy: at(NOW + 60_000) });
        expect(writes).toEqual(NONE);
      });

      it('issues one microsecond before validUntil', async () => {
        const { result, writes } = await windowed(at(NOW - 60_000), at(NOW, 1));
        expect(result.ok).toBe(true);
        expect(writes).toEqual({ numbersConsumed: 1, queueInserts: 1, stockReductions: 1 });
      });

      it('refuses exactly at validUntil and names the signed deadline', async () => {
        const { result, writes } = await windowed(at(NOW - 60_000), at(NOW));
        expect(result).toEqual({ ok: false, reason: 'OFFLINE_PERMIT_EXPIRED', reconnectBy: at(NOW) });
        expect(writes).toEqual(NONE);
      });

      it('refuses a sale dated before the window even though now is inside it', async () => {
        const { result, writes } = await windowed(at(NOW - 100), at(NOW + 60_000), new Date(NOW - 500).toISOString());
        expect(result).toMatchObject({ ok: false, reason: 'OFFLINE_PERMIT_EXPIRED' });
        expect(writes).toEqual(NONE);
      });
    });

    for (const verdict of ['blocked', 'unverified'] as const) {
      it(`refuses while the clock evidence is ${verdict}, whatever the permit says`, async () => {
        const ctx = setup();
        ctx.clock.evaluate.mockResolvedValue(verdict);

        expect(await ctx.service.finalizeAndQueue(makeRequest())).toEqual({ ok: false, reason: 'OFFLINE_CLOCK_INCONSISTENT' });
        expect(zeroWrites(ctx)).toEqual(NONE);
      });
    }

    it('gives the number back when the clock turns inconsistent between the two guards', async () => {
      const ctx = setup();
      ctx.clock.evaluate.mockResolvedValueOnce('consistent').mockResolvedValueOnce('blocked');

      expect(await ctx.service.finalizeAndQueue(makeRequest())).toEqual({ ok: false, reason: 'OFFLINE_CLOCK_INCONSISTENT' });
      expect(ctx.leaseDb.rollbackConsumedInvoiceNumber).toHaveBeenCalledWith(PERMIT_SHOP_ID, 'device-1', '2026-27', 2, {
        leaseId: PERMIT_LEASE_ID, number: 1, clientSaleId: expect.any(String),
      });
      expect(zeroWrites(ctx)).toEqual({ numbersConsumed: 1, queueInserts: 0, stockReductions: 0 });
    });

    it('gives the number back when the session is replaced after the number was taken', async () => {
      const ctx = setup();
      const consume = ctx.leaseDb.consumeNextInvoiceNumber.getMockImplementation()!;
      ctx.leaseDb.consumeNextInvoiceNumber.mockImplementation(async () => {
        const consumed = await consume();
        ctx.sessionState.sessionId = 'abcdefab-cdef-4abc-8def-abcdefabcdee';
        return consumed;
      });

      expect(await ctx.service.finalizeAndQueue(makeRequest())).toEqual({ ok: false, reason: 'OFFLINE_PERMIT_INVALID' });
      expect(ctx.leaseDb.rollbackConsumedInvoiceNumber).toHaveBeenCalledTimes(1);
      expect(zeroWrites(ctx)).toEqual({ numbersConsumed: 1, queueInserts: 0, stockReductions: 0 });
    });

    it('gives the number back when a newer snapshot was promoted after the number was taken', async () => {
      const ctx = setup();
      const info = { snapshotId: PERMIT_SNAPSHOT_ID, completedAt: signedFields.snapshotCompletedAt, schemaVersion: 4, businessTimeZoneId: 'Asia/Kolkata', catalogVersion: 3 };
      ctx.snapshotDb.getUsableSnapshotInfo.mockResolvedValueOnce(info).mockResolvedValue({ ...info, snapshotId: '5a5a5a5a-1111-4222-8333-444444444445' });

      expect(await ctx.service.finalizeAndQueue(makeRequest())).toEqual({ ok: false, reason: 'OFFLINE_PERMIT_INVALID' });
      expect(ctx.leaseDb.rollbackConsumedInvoiceNumber).toHaveBeenCalledTimes(1);
      expect(zeroWrites(ctx)).toEqual({ numbersConsumed: 1, queueInserts: 0, stockReductions: 0 });
    });

    it('asks the last synchronous question inside the queue write and gives the number back on refusal', async () => {
      const ctx = setup();
      let answer: boolean | undefined;
      ctx.queueDb.savePendingSale.mockImplementation(async (_input: unknown, guard: { stillAllowed: () => boolean }) => {
        // The deadline passes after both guards, while the queue database was opening.
        ctx.sessionState.sessionId = '';
        answer = guard.stillAllowed();
        throw new OfflineQueueWriteRefusedError();
      });

      expect(await ctx.service.finalizeAndQueue(makeRequest())).toEqual({ ok: false, reason: 'OFFLINE_PERMIT_INVALID' });
      expect(answer).toBe(false);
      expect(ctx.leaseDb.rollbackConsumedInvoiceNumber).toHaveBeenCalledTimes(1);
      expect(ctx.leaseDb.settleClaim).not.toHaveBeenCalled();
      expect(ctx.shadowDb.reduceQuantity).not.toHaveBeenCalled();
    });

    it('issues nothing when the issuance cannot be serialized on this device', async () => {
      const ctx = setup();
      ctx.lockState.available = false;

      expect(await ctx.service.finalizeAndQueue(makeRequest())).toEqual({ ok: false, reason: 'INVOICE_UNAVAILABLE' });
      expect(zeroWrites(ctx)).toEqual(NONE);
    });

    it('never reuses the number of an interrupted issuance, and settles its claim before taking the next', async () => {
      const ctx = setup();
      ctx.leaseDb.loadPendingClaim.mockResolvedValue({ leaseId: PERMIT_LEASE_ID, number: 1, clientSaleId: 'interrupted-sale' });

      expect((await ctx.service.finalizeAndQueue(makeRequest())).ok).toBe(true);
      expect(ctx.queueDb.getQueuedSale).toHaveBeenCalledWith(PERMIT_SHOP_ID, 'device-1', 'interrupted-sale');
      expect(ctx.leaseDb.settleClaim.mock.calls[0]).toEqual([PERMIT_SHOP_ID, 'device-1', '2026-27', 'interrupted-sale']);
      expect(ctx.leaseDb.rollbackConsumedInvoiceNumber).not.toHaveBeenCalled();
    });

    it('issues nothing while an interrupted issuance cannot be reconciled against the queue', async () => {
      const ctx = setup();
      ctx.leaseDb.loadPendingClaim.mockResolvedValue({ leaseId: PERMIT_LEASE_ID, number: 1, clientSaleId: 'interrupted-sale' });
      ctx.queueDb.getQueuedSale.mockRejectedValue(new Error('queue unreadable'));

      expect(await ctx.service.finalizeAndQueue(makeRequest())).toEqual({ ok: false, reason: 'INVOICE_UNAVAILABLE' });
      expect(ctx.leaseDb.settleClaim).not.toHaveBeenCalled();
      expect(zeroWrites(ctx)).toEqual(NONE);
    });

    it('still confirms a durable invoice when its claim cannot be cleared yet', async () => {
      const ctx = setup();
      ctx.leaseDb.settleClaim.mockRejectedValue(new Error('lease store busy'));

      expect((await ctx.service.finalizeAndQueue(makeRequest())).ok).toBe(true);
      expect(ctx.leaseDb.rollbackConsumedInvoiceNumber).not.toHaveBeenCalled();
      expect(zeroWrites(ctx)).toEqual({ numbersConsumed: 1, queueInserts: 1, stockReductions: 1 });
    });

    it('freezes the exact verified evidence, settles the claim once durable, and keeps every existing contract', async () => {
      const ctx = setup();
      const order: string[] = [];
      ctx.queueDb.savePendingSale.mockImplementation(async () => { order.push('queue'); });
      ctx.leaseDb.settleClaim.mockImplementation(async () => { order.push('settle'); });
      ctx.shadowDb.reduceQuantity.mockImplementation(async () => { order.push('stock'); return 9; });

      const result = await ctx.service.finalizeAndQueue(makeRequest());

      expect(result.ok).toBe(true);
      if (!result.ok) return;
      expect(result.payload.licenceEvidence).toEqual({
        version: 1,
        permitId: signedFields.permitId,
        grantId: PERMIT_GRANT_ID,
        grantSha256: PERMIT_GRANT_SHA256,
        leaseId: PERMIT_LEASE_ID,
        snapshotId: PERMIT_SNAPSHOT_ID,
        keyId: signer.keyId,
        signedPermit,
      });
      expect(ctx.queueDb.savePendingSale.mock.calls[0][0].payload).toBe(result.payload);
      expect(order).toEqual(['queue', 'settle', 'stock']);
      expect(result.payload).toMatchObject({
        issuedByUserId: PERMIT_USER_ID,
        invoiceNumber: 'A/2627/000001',
        idempotencyKey: `offline-sale-${result.payload.clientSaleId}`,
        pricing: { calculationVersion: 1 },
        document: { number: 'A/2627/000001' },
      });
    });
  });

  function makeRequest(): OfflineFinalizeRequest {
    return {
      shopId: PERMIT_SHOP_ID,
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
      expiryDate: null, manufacturingDate: null,
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
