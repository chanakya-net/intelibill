import {
  HubConnectionFactory,
  HubConnectionLike,
  HubConnectionRequest,
  HubRetryPolicy,
} from '../../app/core/services/hub-connection.factory';

/**
 * One connection the factory handed out. Kept after it is replaced, so a spec can fire a handler
 * belonging to a socket the service has already discarded — which is what a callback delayed across
 * a shop switch looks like.
 */
export class FakeHubConnection implements HubConnectionLike {
  readonly handlers = new Map<string, (payload: never) => void>();

  startCalls = 0;
  stopCalls = 0;

  /** Set to make `start()` reject, as a refused or unreachable handshake would. */
  startFailure: Error | null = null;

  on<TPayload>(event: string, handler: (payload: TPayload) => void): void {
    this.handlers.set(event, handler as (payload: never) => void);
  }

  async start(): Promise<void> {
    this.startCalls += 1;
    if (this.startFailure) {
      throw this.startFailure;
    }
  }

  async stop(): Promise<void> {
    this.stopCalls += 1;
  }

  emit<TPayload>(event: string, payload: TPayload): void {
    (this.handlers.get(event) as ((payload: TPayload) => void) | undefined)?.(payload);
  }
}

/**
 * A factory a spec provides in place of the real one.
 *
 * Substituting this service rather than the `@microsoft/signalr` module is deliberate: a module
 * replacement only holds while the spec file is evaluated before anything else pulls the library in,
 * which is why the module-mocked version of these suites passed alone and failed in a full run.
 */
export class FakeHubConnectionFactory extends HubConnectionFactory {
  readonly created: FakeHubConnection[] = [];
  readonly urls: string[] = [];
  readonly retryPolicies: HubRetryPolicy[] = [];

  override create(request: HubConnectionRequest): HubConnectionLike {
    this.urls.push(request.url);
    this.retryPolicies.push(request.retryPolicy);
    const connection = new FakeHubConnection();
    this.created.push(connection);
    return connection;
  }

  get latest(): FakeHubConnection {
    return this.created[this.created.length - 1];
  }
}
