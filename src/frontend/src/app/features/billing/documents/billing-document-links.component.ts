import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonDirective } from 'primeng/button';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { BlobDownloadService } from '../../../shared/utils/blob-download.service';
import { billingDocumentFileName, type BillingDocument } from './billing-document.models';
import { BillingDocumentService } from './billing-document.service';

@Component({
  selector: 'app-billing-document-links',
  imports: [TranslocoPipe, ButtonDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section
      class="grid gap-2 min-w-0 [overflow-wrap:anywhere]"
      data-testid="billing-documents"
      [attr.aria-label]="'billing.documents.heading' | transloco"
    >
      <h4 data-testid="billing-documents-heading">{{ 'billing.documents.heading' | transloco }}</h4>
      <p data-testid="billing-documents-purpose">{{ 'billing.documents.purpose' | transloco }}</p>
      <button
        pButton
        type="button"
        data-testid="billing-documents-list"
        [disabled]="busy()"
        [label]="(loaded() ? 'billing.documents.refresh' : 'billing.documents.view') | transloco"
        (click)="load()"
      ></button>
      @if (loaded() && rows().length === 0) {
        <p data-testid="billing-documents-empty">{{ 'billing.documents.empty' | transloco }}</p>
      }
      @for (doc of rows(); track doc.versionId) {
        <div
          class="grid gap-1 min-w-0"
          data-testid="billing-document-item"
          [attr.data-version]="doc.version"
          [attr.data-version-id]="doc.versionId"
        >
          <dl class="grid gap-1 min-w-0" data-testid="billing-document-metadata">
            <div class="min-w-0">
              <dt>{{ 'billing.documents.status' | transloco }}</dt>
              <dd class="m-0 min-w-0" data-testid="billing-document-status">
                {{
                  (doc.versionId === currentVersionId()
                    ? 'billing.documents.current'
                    : 'billing.documents.historical') | transloco
                }}
              </dd>
            </div>
            <div class="min-w-0">
              <dt>{{ 'billing.documents.issuer' | transloco }}</dt>
              <dd class="m-0 min-w-0" data-testid="billing-document-issuer">{{ doc.issuerName }}</dd>
            </div>
            <div class="min-w-0">
              <dt>{{ 'billing.documents.reference' | transloco }}</dt>
              <dd class="m-0 min-w-0" data-testid="billing-document-reference">{{ doc.issuerReference }}</dd>
            </div>
            <div class="min-w-0">
              <dt>{{ 'billing.documents.issuedOn' | transloco }}</dt>
              <dd class="m-0 min-w-0" data-testid="billing-document-issued-on">{{ doc.issuedOn }}</dd>
            </div>
            <div class="min-w-0">
              <dt>{{ 'billing.documents.version' | transloco }}</dt>
              <dd class="m-0 min-w-0" data-testid="billing-document-version">{{ doc.version }}</dd>
            </div>
            @if (doc.supersedesVersionId) {
              <div class="min-w-0">
                <dt>{{ 'billing.documents.supersedes' | transloco }}</dt>
                <dd class="m-0 min-w-0" data-testid="billing-document-supersedes">
                  <code class="break-all">{{ doc.supersedesVersionId }}</code>
                </dd>
              </div>
            }
            <div class="min-w-0">
              <dt>{{ 'billing.documents.hash' | transloco }}</dt>
              <dd class="m-0 min-w-0" data-testid="billing-document-hash"><code class="break-all">{{ doc.sha256 }}</code></dd>
            </div>
          </dl>
          <button
            pButton
            type="button"
            data-testid="billing-document-download"
            [disabled]="busy()"
            [label]="'billing.documents.download' | transloco"
            (click)="download(doc)"
          ></button>
        </div>
      }
      @if (nextBeforeVersion() !== null) {
        <button
          pButton
          type="button"
          severity="secondary"
          [outlined]="true"
          data-testid="billing-documents-older"
          [disabled]="busy()"
          [label]="'billing.documents.loadOlder' | transloco"
          (click)="loadOlder()"
        ></button>
      }
      @if (error()) {
        <p role="alert" data-testid="billing-documents-error">{{ 'billing.documents.error' | transloco }}</p>
      }
    </section>
  `,
})
export class BillingDocumentLinksComponent {
  readonly account = input.required<string>();
  readonly order = input.required<string>();

  private readonly context = inject(ShopContextService);
  private readonly api = inject(BillingDocumentService);
  private readonly blobs = inject(BlobDownloadService);
  private readonly scope = computed(() => {
    const current = this.context.scope();
    return current ? { ...current, account: this.account(), order: this.order() } : null;
  });

  protected readonly rows = signal<readonly BillingDocument[]>([]);
  protected readonly currentVersionId = computed(() => this.rows()[0]?.versionId ?? null);
  protected readonly nextBeforeVersion = computed(() => {
    const list = this.rows();
    const oldest = list[list.length - 1];
    return oldest && oldest.version > 1 ? oldest.version : null;
  });
  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal(false);
  private destroyed = false;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
    });
    effect(() => {
      this.scope();
      this.rows.set([]);
      this.loaded.set(false);
      this.busy.set(false);
      this.error.set(false);
    });
  }

  protected load() {
    void this.run(async (scope) => {
      const documents = await this.api.list(scope, scope.order);
      if (this.destroyed || this.scope() !== scope) return;
      this.rows.set(documents);
      this.loaded.set(true);
    });
  }

  protected loadOlder() {
    const before = this.nextBeforeVersion();
    if (before === null) return;
    void this.run(async (scope) => {
      const older = await this.api.list(scope, scope.order, before);
      if (this.destroyed || this.scope() !== scope || this.nextBeforeVersion() !== before) return;
      if (older.length === 0) return;
      const existing = this.rows();
      const tail = existing[existing.length - 1];
      if (!tail || older[0].version >= tail.version) {
        this.error.set(true);
        return;
      }
      this.rows.set([...existing, ...older]);
    });
  }

  protected download(doc: BillingDocument) {
    void this.run(async (scope) => {
      const blob = await this.api.content(scope, scope.order, doc);
      if (!this.destroyed && this.scope() === scope) {
        this.blobs.download(blob, billingDocumentFileName(doc.versionId));
      }
    });
  }

  private async run(work: (scope: NonNullable<ReturnType<typeof this.scope>>) => Promise<void>) {
    const scope = this.scope();
    if (!scope || this.busy()) return;
    this.busy.set(true);
    this.error.set(false);
    try {
      await work(scope);
    } catch {
      if (!this.destroyed && this.scope() === scope) this.error.set(true);
    } finally {
      if (!this.destroyed && this.scope() === scope) this.busy.set(false);
    }
  }
}
