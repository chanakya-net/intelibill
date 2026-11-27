import { Injectable } from '@angular/core';
import * as signalR from '@microsoft/signalr';

/** How long to wait before the next reconnect attempt, or `null` to stop trying. */
export interface HubRetryPolicy {
  nextRetryDelayInMilliseconds(context: {
    readonly previousRetryCount: number;
    readonly elapsedMilliseconds: number;
    readonly retryReason: unknown;
  }): number | null;
}

/** The part of a SignalR connection this application uses. */
export interface HubConnectionLike {
  on<TPayload>(event: string, handler: (payload: TPayload) => void): void;
  start(): Promise<void>;
  stop(): Promise<void>;
}

export interface HubConnectionRequest {
  readonly url: string;
  readonly accessTokenFactory: () => string;
  readonly retryPolicy: HubRetryPolicy;
}

/**
 * The one place the SignalR client library is constructed.
 *
 * It exists so that connection behaviour can be tested by substituting this service rather than by
 * replacing the `@microsoft/signalr` module. A module replacement only holds when the spec file
 * happens to be evaluated before anything else pulls the library in, which is why the suite that
 * relied on one was skipped for a pipeline-only race.
 */
@Injectable({ providedIn: 'root' })
export class HubConnectionFactory {
  create(request: HubConnectionRequest): HubConnectionLike {
    return new signalR.HubConnectionBuilder()
      .withUrl(request.url, { accessTokenFactory: request.accessTokenFactory })
      .withAutomaticReconnect(request.retryPolicy)
      .build();
  }
}
