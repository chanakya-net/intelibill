import http from 'node:http';
import type { AddressInfo } from 'node:net';
import { startReleaseServer } from './release-server';
import type {
  PurchaseOrderDetail,
  PurchaseOrderStatus,
  ReceivePurchaseOrderRequest,
} from '../../../src/app/features/purchase-orders/services/purchase-order.service';

/** UI-only API fixture. Real transaction/replay/lineage proof lives in PurchaseOrdersControllerTests.
 * Static assets and workers are served by the unchanged protected-cache release server. */
export const PO = '10000000-0000-4000-8000-000000000001';
export const OTHER_PO = '10000000-0000-4000-8000-000000000002';
export const SHOP_A = '20000000-0000-4000-8000-000000000001';
export const SHOP_B = '20000000-0000-4000-8000-000000000002';
export const USER = '30000000-0000-4000-8000-000000000001';
const LINE = '40000000-0000-4000-8000-000000000001';
export const shops = [SHOP_A, SHOP_B].map((shopId, i) => ({
  shopId,
  shopName: `Shop ${i ? 'B' : 'A'}`,
  role: 'Owner',
  isDefault: !i,
  lastUsedAt: null,
}));
export function session(shopId = SHOP_A) {
  const jwt = `e30.${Buffer.from(JSON.stringify({ sub: USER, active_shop_id: shopId, exp: 4102444800 })).toString('base64url')}.fixture`;
  return {
    accessToken: jwt,
    refreshToken: 'fixture-refresh',
    accessTokenExpiresAt: '2099-01-01T00:00:00Z',
    refreshTokenExpiresAt: '2099-02-01T00:00:00Z',
    rememberMe: true,
    user: {
      id: USER,
      email: 'receiving@example.test',
      phoneNumber: null,
      firstName: 'Receiving',
      lastName: 'Owner',
    },
    activeShopId: shopId,
    shops,
    sessionId: 'fixture-session',
  };
}
export type Mode = 'accept' | 'reject' | 'accept-drop' | 'drop' | 'retry-404' | 'hold';
export async function startPurchaseReceivingServer(root: string) {
  const assets = await startReleaseServer(root);
  const orders = new Map<string, PurchaseOrderDetail>();
  const operations = new Map<
    string,
    { payload: string; targetId: string; response: PurchaseOrderDetail }
  >();
  const posts: {
    poId: string;
    payload: ReceivePurchaseOrderRequest;
    raw: string;
    authorization?: string;
  }[] = [];
  const lookups: string[] = [];
  const held: (() => void)[] = [];
  let mode: Mode = 'accept';
  function order(id: string): PurchaseOrderDetail {
    let value = orders.get(id);
    if (!value) {
      value = {
        purchaseOrderId: id,
        purchaseOrderNumber: id === PO ? 'PO-RECEIVING-1' : 'PO-RECEIVING-2',
        status: 'Placed',
        supplierId: 'supplier-1',
        supplierName: 'Fixture supplier',
        supplierReference: null,
        receivedQuantity: 0,
        orderDate: '2026-11-07',
        expectedDeliveryDate: null,
        supplierReferenceNumber: null,
        notes: null,
        lines: [
          {
            lineId: LINE,
            itemId: 'item-1',
            description: 'Widget',
            expectedQuantity: 10,
            receivedQuantity: 0,
            remainingQuantity: 10,
            unitCost: 10,
            lineTotal: 100,
          },
        ],
        expectedTotal: 100,
        createdAt: '2026-11-07T08:00:00Z',
        cancellationReason: null,
        receipts: [],
      };
      orders.set(id, value);
    }
    return value;
  }
  function send(res: http.ServerResponse, status: number, value: unknown) {
    res.writeHead(status, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
    res.end(JSON.stringify(value));
  }
  function loseResponse(res: http.ServerResponse) {
    // Headers + an incomplete body prevent Chromium retrying an empty reused TCP connection.
    res.writeHead(200, {
      'Content-Type': 'application/json',
      'Cache-Control': 'no-store',
      'Content-Length': '999999',
    });
    res.write('{');
    setImmediate(() => res.destroy());
  }
  const server = http.createServer(async (req, res) => {
    const path = new URL(req.url!, 'http://fixture.test').pathname;
    if (!path.startsWith('/api/') && !path.startsWith('/hubs/')) {
      const upstream = http.request(
        `${assets.origin}${req.url}`,
        { method: req.method, headers: req.headers },
        (response) => {
          res.writeHead(response.statusCode ?? 500, response.headers);
          response.pipe(res);
        },
      );
      upstream.on('error', () => res.destroy());
      req.pipe(upstream);
      return;
    }
    if (path.startsWith('/api/expiry-policies')) {
      const policy = { version: 1, mode: 'Ignore', expiryRequired: false, sellThroughExpiryDate: true, warningDays: 7, overrideAllowed: false };
      return send(res, 200, { defaultPolicy: policy, overridePolicy: null, effectivePolicy: policy, catalogVersion: 1 });
    }
    if (path === '/api/ping') return send(res, 200, { serverTime: new Date().toISOString() });
    if (path === '/api/shops/me') return send(res, 200, shops);
    if (path === '/api/shops/switch') {
      const chunks = [];
      for await (const chunk of req) chunks.push(chunk);
      const { shopId } = JSON.parse(Buffer.concat(chunks).toString());
      return send(res, 200, { ...session(shopId), scopeVersion: 1 });
    }
    if (path.startsWith('/api/shops/'))
      return send(res, 200, {
        id: path.split('/').pop(),
        name: 'Fixture shop',
        businessTimeZone: 'Asia/Kolkata',
      });
    if (path === '/api/purchase-orders')
      return send(res, 200, {
        items: [order(PO), order(OTHER_PO)].map((o) => ({
          ...o,
          lineCount: 1,
          expectedQuantity: 10,
        })),
        totalCount: 2,
        pageNumber: 1,
        pageSize: 20,
      });
    const lookup = path.match(/^\/api\/financial-operations\/purchase-order-receipt\/(.+)$/);
    if (lookup) {
      lookups.push(lookup[1]);
      const op = operations.get(lookup[1]);
      return op
        ? send(res, 200, {
            operationType: 'purchase-order-receipt',
            operationKey: lookup[1],
            targetId: op.targetId,
            response: op.response,
          })
        : send(res, 404, { code: 'Operation.NotFound' });
    }
    const detail = path.match(/^\/api\/purchase-orders\/([^/]+)(\/receipts)?$/);
    if (detail && req.method === 'GET') return send(res, 200, order(detail[1]));
    if (detail?.[2] && req.method === 'POST') {
      const chunks = [];
      for await (const chunk of req) chunks.push(chunk);
      const raw = Buffer.concat(chunks).toString();
      const payload = JSON.parse(raw) as ReceivePurchaseOrderRequest;
      const poId = detail[1];
      posts.push({ poId, payload, raw, authorization: req.headers.authorization });
      if (mode === 'reject')
        return send(res, 400, {
          code: 'PurchaseOrder.ReceiptBatchAlreadyExists',
          rowErrors: payload.lines.map((l, i) => ({
            code: 'PurchaseOrder.ReceiptBatchAlreadyExists',
            clientLineId: l.clientLineId,
            lineIndex: i,
            field: 'batchNumber',
          })),
        });
      if (mode === 'drop') {
        loseResponse(res);
        return;
      }
      if (mode === 'retry-404') return send(res, 404, { code: 'PurchaseOrder.NotFound' });
      const complete = () => {
        const existing = operations.get(payload.operationId!);
        if (existing)
          return existing.payload === raw
            ? send(res, 200, existing.response)
            : send(res, 409, { code: 'Operation.PayloadMismatch' });
        const original = order(poId);
        const quantity = payload.lines.reduce((a, l) => a + l.quantity, 0);
        const result: PurchaseOrderDetail = {
          ...original,
          status: quantity === 10 ? 'Received' : 'PartiallyReceived',
          receivedQuantity: quantity,
          lines: original.lines.map((l) => ({
            ...l,
            receivedQuantity: quantity,
            remainingQuantity: 10 - quantity,
          })),
          receipts: [
            {
              receiptId: 'receipt-1',
              receiptNumber: 'POR-FIXTURE-1',
              receivedAt: '2026-11-07T09:00:00Z',
              referenceNumber: payload.referenceNumber,
              notes: payload.notes,
              lines: payload.lines.map((l, i) => ({
                ...l,
                receiptLineId: `receipt-line-${i}`,
                itemId: 'item-1',
                inventoryBatchId: `batch-${i}`,
                stockTransactionId: `stock-${i}`,
                unitCost: l.totalPurchaseCost / l.quantity,
              })),
            },
          ],
        };
        orders.set(poId, result);
        operations.set(payload.operationId!, {
          payload: raw,
          targetId: poId,
          response: structuredClone(result),
        });
        if (mode === 'accept-drop') loseResponse(res);
        else send(res, 200, result);
      };
      if (mode === 'hold') held.push(complete);
      else complete();
      return;
    }
    if (path.includes('/items') || path.includes('/suppliers') || path.includes('/notifications'))
      return send(res, 200, []);
    return send(res, 200, {});
  });
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  return {
    origin: `http://127.0.0.1:${(server.address() as AddressInfo).port}`,
    /** The static release server behind this one (worker, manifests, request log). */
    assets,
    posts,
    operations,
    lookups,
    setMode(value: Mode) {
      mode = value;
    },
    setStatus(id: string, status: PurchaseOrderStatus) {
      orders.set(id, { ...order(id), status });
    },
    changeDescription(id: string) {
      orders.set(id, {
        ...order(id),
        lines: order(id).lines.map((l) => ({
          ...l,
          description: 'Refreshed Widget',
          unitCost: 99,
        })),
      });
    },
    releaseHeld() {
      held.splice(0).forEach((fn) => fn());
    },
    async close() {
      server.closeAllConnections();
      await new Promise<void>((resolve) => server.close(() => resolve()));
      await assets.close();
    },
  };
}
