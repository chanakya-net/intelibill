import type { IncomingMessage, ServerResponse } from 'node:http';
import { CUSTOMER, SALE } from './accessibility-localization-fixtures';
import { startReleaseServer } from './release-server';
import { session, SHOP_A, SHOP_B, shops } from './purchase-receiving-server';

export const INVOICE_A = SALE.invoiceNumber;
export const INVOICE_B = 'B/2627/000222';
export const PAYMENT_INTENTS_KEY = 'intelibill.financial-operation-intents.v1';

export async function startLicensingHistoryFixture(root: string) {
  const state = { restricted: false, verificationUnavailable: false, holdExport: false, holdPrintAuthority: false, printVerificationUnavailable: false };
  const heldAuthority: (() => void)[] = [];
  const heldExports: (() => void)[] = [];
  const posts: Record<string, unknown>[] = [];
  const lookups: string[] = [];
  const receipt = {
    entryId: 'payment-entry', receiptId: 'payment-receipt', amount: 25, runningBalance: 25,
    entryDate: '2026-11-21',
  };
  const saleFor = (shopId: string) => shopId === SHOP_B
    ? { ...SALE, invoiceNumber: INVOICE_B, document: { ...SALE.document, number: INVOICE_B } }
    : SALE;

  async function handle(req: IncomingMessage, res: ServerResponse, url: URL): Promise<boolean> {
    const shopId = readShop(req);
    const path = url.pathname;
    const json = (body: unknown, status = 200) => respond(res, status, body);
    const security = { code: 'Licence.SecuritySuspended', status: 403 };
    if (path === '/api/tills/capabilities') {
      return json({ enabled: false, mixedTenderEnabled: false, businessTimeZoneId: 'Asia/Kolkata' });
    }
    if (path === '/api/auth/token/refresh') return json(session(shopId || SHOP_A));
    if (path === '/api/shops/me') return json(shops);
    if (path === '/api/shops/switch' && req.method === 'POST') {
      const { shopId: target } = await readJson(req);
      return json(session(String(target)));
    }
    if (path.endsWith('/trading-authority')) {
      const printTab = req.headers['x-fixture-print-tab'] === 'true';
      const ordinaryExpiry = () => json({ code: 'Licence.Expired', enforced: true, status: 403 }, 403);
      if (printTab && state.holdPrintAuthority) {
        state.holdPrintAuthority = false;
        heldAuthority.push(ordinaryExpiry);
        return true;
      }
      if (state.verificationUnavailable || printTab && state.printVerificationUnavailable)
        return json({ code: 'Licence.VerificationUnavailable' }, 503);
      return state.restricted && shopId === SHOP_A
        ? json({ ...security, enforced: true }, 403)
        : ordinaryExpiry();
    }

    const isBusiness = /^\/api\/(sales|exports|items|shops|customers|payments|financial-operations)(\/|$)/.test(path);
    if (!isBusiness) return json({ code: 'Fixture.NotFound' }, 404);
    if (state.restricted && shopId === SHOP_A) return json(security, 403);

    if (path === '/api/items/stream') {
      res.writeHead(200, { 'Content-Type': 'application/x-ndjson', 'Cache-Control': 'no-store' }).end();
      return true;
    }
    if (path.startsWith('/api/shops/')) {
      return json({
        shopId, name: 'Fixture merchant', address: '12 Market Rd', city: 'Bengaluru', pincode: '560001',
        state: 'Karnataka', businessTimeZoneId: 'Asia/Kolkata',
      });
    }
    if (path === '/api/sales') {
      return json({
        items: [{ ...saleFor(shopId), status: 'partiallyPaid', returnNumbers: [], itemCount: 1, refundAmount: 0, dueReductionAmount: 0 }],
        summary: { periodSales: 100, invoiceCount: 1, refundAmount: 0 },
        totalCount: 1, pageNumber: 1, pageSize: 20,
      });
    }
    if (path === `/api/sales/${SALE.saleId}`) return json(saleFor(shopId));
    if (path === '/api/exports/sales') {
      const send = () => {
        res.writeHead(200, {
          'Cache-Control': 'no-store', 'Content-Type': 'application/pdf',
          'Content-Disposition': 'attachment; filename="issued-history.pdf"',
        }).end(`%PDF-1.4\n${saleFor(shopId).invoiceNumber}\n%%EOF`);
      };
      if (state.holdExport) heldExports.push(send);
      else send();
      return true;
    }

    if (path === '/api/customers/directory') return json({ items: [CUSTOMER], totalCount: 1, pageNumber: 1, pageSize: 20 });
    if (path === '/api/customers') return json([CUSTOMER]);
    if (path === `/api/customers/${CUSTOMER.customerId}`) return json(CUSTOMER);
    const posted = shopId === SHOP_A && posts.length > 0;
    const outstandingDue = posted ? 25 : 50;
    if (path.endsWith('/account')) {
      return json({
        ...CUSTOMER, outstandingDue, openingBalance: 0, closingBalance: outstandingDue,
        unallocatedCredit: 0, sales: [{ ...saleFor(shopId), currentOutstandingAmount: outstandingDue }],
        ledgerEntries: [], paymentHistory: [],
        receipts: posted ? [{
          receiptId: receipt.receiptId, amount: receipt.amount, method: 'Cash',
          externalReference: null, businessDate: posts[0]['paymentDate'], ledgerEntryId: receipt.entryId,
          isLegacy: false, isReversed: false, allocatedAmount: 25, unallocatedAmount: 0,
        }] : [],
      });
    }
    if (path === '/api/payments/balances') return json({ remainingSettlementDue: outstandingDue });
    if (path.endsWith('/payments') && req.method === 'POST') {
      posts.push(await readJson(req));
      return json({ code: 'Licence.DeliveryUnconfirmed', status: 503 }, 503);
    }
    if (path.startsWith('/api/financial-operations/customer-payment/')) {
      const operationKey = path.split('/').at(-1)!;
      lookups.push(operationKey);
      return shopId === SHOP_A && posts[0]?.['operationKey'] === operationKey
        ? json({ operationType: 'customer-payment', targetId: CUSTOMER.customerId, operationKey, response: { ...receipt, entryDate: posts[0]['paymentDate'] } })
        : json({ code: 'Operation.NotFound' }, 404);
    }
    return json({ code: 'Fixture.NotFound' }, 404);
  }

  return { server: await startReleaseServer(root, handle), state, heldAuthority, heldExports, posts, lookups };
}

function respond(res: ServerResponse, status: number, body: unknown): true {
  res.writeHead(status, {
    'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json',
    'Cache-Control': 'no-store',
  }).end(JSON.stringify(body));
  return true;
}

function readShop(req: IncomingMessage): string {
  const claims = req.headers.authorization?.split('.')[1];
  return claims ? JSON.parse(Buffer.from(claims, 'base64url').toString()).active_shop_id : '';
}

async function readJson(req: IncomingMessage): Promise<Record<string, unknown>> {
  const chunks: Buffer[] = [];
  for await (const chunk of req) chunks.push(chunk);
  return JSON.parse(Buffer.concat(chunks).toString());
}
