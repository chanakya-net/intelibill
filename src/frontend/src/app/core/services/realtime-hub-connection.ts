import { effect, inject } from '@angular/core';

import { AuthService } from '../auth/auth.service';
import { HUB_BASE_URL } from '../auth/auth.constants';
import { HubConnectionFactory, HubConnectionLike } from './hub-connection.factory';

/** Backoff for an ordinary network interruption, in milliseconds. */
const RECONNECT_DELAYS_MS = [0, 2_000, 10_000, 30_000];

/**
 * Whether the last failure was the server refusing us rather than the network dropping us. A
 * refused negotiate comes back as an HttpError carrying the status.
 */
function refusedForAuthorization(reason: unknown): boolean {
  const status = (reason as { statusCode?: number } | undefined)?.statusCode;
  return status === 401 || status === 403;
}

export interface ShopScopedPayload {
  readonly shopId: string;
}

/**
 * The connection lifecycle both notification hubs share.
 *
 * Three things it exists to get right, which a hand-rolled `start()` per service did not:
 *
 * - The connection is keyed to the token and shop it was opened with, so a shop switch or a token
 *   renewal rebuilds it instead of leaving a socket authorized for the previous scope.
 * - Every handler is bound to the generation of the connection that registered it. A socket we have
 *   already replaced can still deliver a frame that was in flight, and after a switch the view it
 *   would update belongs to a different shop.
 * - A revoked session stops reconnecting, while a network outage keeps retrying. SignalR cannot tell
 *   those apart on its own, and treating a refusal as an outage means hammering a handshake that can
 *   only be refused again.
 */
export abstract class RealtimeHubConnection<TPayload extends ShopScopedPayload> {
  protected readonly authService = inject(AuthService);
  private readonly connections = inject(HubConnectionFactory);

  private connection: HubConnectionLike | null = null;
  private connectionKey = '';
  private generation = 0;
  private syncPromise: Promise<void> | null = null;
  private syncRequested = false;

  /** Path under the hub origin, e.g. `/hubs/products`. */
  protected abstract readonly hubPath: string;

  /** Server-sent method name this hub delivers. */
  protected abstract readonly eventName: string;

  protected abstract readonly logPrefix: string;

  /** Called only for a payload from the current connection, for the current shop. */
  protected abstract handle(payload: TPayload): void;

  constructor() {
    effect(() => {
      this.authService.session();
      void this.syncConnection();
    });
  }

  async startConnection(): Promise<void> {
    await this.syncConnection();
  }

  async stopConnection(): Promise<void> {
    this.syncRequested = false;
    this.connectionKey = '';
    await this.disposeConnection();
  }

  private async syncConnection(): Promise<void> {
    this.syncRequested = true;
    if (this.syncPromise) {
      return this.syncPromise;
    }

    this.syncPromise = this.flushConnectionSync();
    try {
      await this.syncPromise;
    } finally {
      this.syncPromise = null;
    }
  }

  private async flushConnectionSync(): Promise<void> {
    try {
      while (this.syncRequested) {
        this.syncRequested = false;
        await this.applyConnectionState();
      }
    } catch (err) {
      console.error(`${this.logPrefix} Sync failed:`, err);
    }
  }

  private async applyConnectionState(): Promise<void> {
    const session = this.authService.session();
    const activeShopId = session?.activeShopId ?? '';
    const accessToken = session?.accessToken ?? '';
    const desiredKey = activeShopId && accessToken ? `${activeShopId}::${accessToken}` : '';

    if (!desiredKey) {
      this.connectionKey = '';
      await this.disposeConnection();
      return;
    }

    if (this.connection && this.connectionKey === desiredKey) {
      return;
    }

    await this.disposeConnection();
    this.connectionKey = desiredKey;

    // Captured, not read back from the field: the handler below must compare against the generation
    // it was registered under, which is what makes a callback from a replaced socket identifiable.
    const generation = ++this.generation;

    this.connection = this.connections.create({
      url: `${HUB_BASE_URL}${this.hubPath}`,
      accessTokenFactory: () => this.authService.getAccessToken() ?? '',
      retryPolicy: {
        nextRetryDelayInMilliseconds: (context) => {
          if (!this.hasUsableSession()) {
            return null;
          }

          // The first attempt after a server-side abort has no status to read — the close itself is
          // all we know — so one attempt is spent learning which kind of failure this is. Once the
          // handshake comes back refused, retrying cannot change the answer.
          if (refusedForAuthorization(context.retryReason)) {
            return null;
          }

          return RECONNECT_DELAYS_MS[
            Math.min(context.previousRetryCount, RECONNECT_DELAYS_MS.length - 1)
          ];
        },
      },
    });

    this.connection.on<TPayload>(this.eventName, (payload) => {
      if (generation !== this.generation) {
        return;
      }

      const currentShopId = this.authService.session()?.activeShopId;
      if (!currentShopId || payload.shopId !== currentShopId) {
        return;
      }

      this.handle(payload);
    });

    try {
      await this.connection.start();
    } catch (err) {
      console.error(`${this.logPrefix} Connection failed:`, err);
      await this.disposeConnection();
    }
  }

  private async disposeConnection(): Promise<void> {
    // Retired before the socket is asked to stop, so a frame delivered during the stop is already
    // out of date by the generation check.
    this.generation += 1;

    if (!this.connection) {
      return;
    }

    const connection = this.connection;
    this.connection = null;
    try {
      await connection.stop();
    } catch (err) {
      console.error(`${this.logPrefix} Stop failed:`, err);
    }
  }

  private hasUsableSession(): boolean {
    const session = this.authService.session();
    return !!session?.activeShopId && !!this.authService.getAccessToken();
  }
}
