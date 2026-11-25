import { NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { problemCode } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import { AdminSessionService } from '../auth/admin-session.service';
import { BillingDocumentApi, invoiceScopeId, type InvoiceList, type InvoicePreview, type InvoiceScope } from './billing-document-api.service';
import { BillingDocumentDrafts } from './billing-document-drafts';
import { InvoiceInputError, prepareInvoice, type InvoiceIntent } from './billing-document-file';

type Invocation = InvoiceScope & { current: () => boolean };
const definiteRefusals = new Map([
  ['BillingDocument.Invalid', 'Check the invoice metadata and issue date.'],
  ['BillingDocument.ContentInvalid', 'Select a valid PDF.'],
  ['BillingDocument.ContentMismatch', 'The PDF differs from the reviewed file.'],
  ['BillingDocument.TooLarge', 'PDFs must be no larger than 10 MiB.'],
  ['BillingDocument.MediaTypeUnsupported', 'Select a PDF file.'],
  ['BillingDocument.OrderNotFound', 'The accepted order was not found for this account.'],
  ['BillingDocument.AlreadyAttached', 'An accounting invoice already exists for this order.'],
  ['BillingDocument.PreviewStale', 'Review the order and invoice again before attaching.'],
]);

@Component({
  selector: 'adm-billing-document-page',
  imports: [ButtonDirective, NgTemplateOutlet, RouterLink],
  templateUrl: './billing-document-page.component.html',
  styleUrl: '../form-page.css',
  styles: `
    section, dd, code { overflow-wrap: anywhere; }
    fieldset { border: 0; padding: 0; margin: 0; display: grid; gap: 0.9rem; min-width: 0; }
    @media (min-width: 40rem) { dl { display: grid; grid-template-columns: 10rem minmax(0, 1fr); gap: 0.35rem 1rem; } }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BillingDocumentPageComponent {
  private readonly api = inject(BillingDocumentApi);
  private readonly drafts = inject(BillingDocumentDrafts);
  private readonly session = inject(AdminSessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly params = toSignal(this.route.queryParamMap);

  protected readonly account = computed(() => invoiceScopeId(this.params()?.get('account') ?? null));
  protected readonly order = computed(() => invoiceScopeId(this.params()?.get('order') ?? null));
  protected readonly allowed = computed(() => this.session.capabilities().includes('billing'));
  protected readonly view = signal<InvoiceList | null>(null);
  protected readonly preview = signal<InvoicePreview | null>(null);
  protected readonly pending = signal<InvoiceIntent | null>(null);
  protected readonly busy = signal(false);
  protected readonly confirmed = signal(false);
  protected readonly saved = signal(false);
  protected readonly reauthenticate = signal(false);
  protected readonly error = signal('');
  private revision = 0;
  private generation = 0;
  private reviewedBytes: ArrayBuffer | null = null;

  constructor() {
    effect(() => {
      this.account();
      this.order();
      this.session.adminId();
      this.session.sessionId();
      this.allowed();
      untracked(() => this.restore());
    });
    inject(DestroyRef).onDestroy(() => this.generation++);
  }

  protected search(event: Event, accountInput: string, orderInput: string) {
    event.preventDefault();
    const account = invoiceScopeId(accountInput);
    const order = invoiceScopeId(orderInput);
    if (!account || !order) {
      this.error.set('Enter a valid billing account ID and accepted order ID.');
      return;
    }
    if (this.account() === account && this.order() === order) this.restore();
    else void this.router.navigate([], { relativeTo: this.route, queryParams: { account, order } });
  }

  protected clearPreview() {
    this.reviewedBytes = null;
    this.preview.set(null);
    this.confirmed.set(false);
  }

  protected review(event: Event, file: File | null) {
    event.preventDefault();
    const form = event.target as HTMLFormElement;
    if (!this.attachable()) return;
    this.clearPreview();
    void this.run(async scope => {
      const { bytes, metadata } = await prepareInvoice(file, form);
      if (!scope.current()) return;
      const preview = await this.api.preview(scope, metadata);
      if (!scope.current()) return;
      const order = this.view()?.order;
      if (preview.order.snapshotHash !== order?.snapshotHash || preview.order.revision !== order.revision)
        throw new InvoiceInputError('The order changed. Load the order and review the invoice again.');
      this.reviewedBytes = bytes;
      this.preview.set(preview);
    });
  }

  protected attach() {
    const preview = this.preview();
    const bytes = this.reviewedBytes;
    if (!preview || !bytes || !this.confirmed() || !this.attachable()) return;
    const command = { operationKey: crypto.randomUUID(), metadata: preview.metadata, previewDigest: preview.previewDigest, bytes };
    void this.run(async scope => {
      const draft = await this.drafts.claim(scope, this.revision, command);
      if (!scope.current()) return;
      this.revision = draft.revision;
      this.pending.set(draft.pending ?? null);
      this.clearPreview();
      if (draft.pending?.operationKey === command.operationKey) await this.finish(scope, command, false);
      else if (!draft.pending) await this.load(scope);
    });
  }

  protected recover(lookup: boolean) {
    const intent = this.pending();
    if (intent) void this.run(scope => this.finish(scope, intent, lookup));
  }

  protected money(amountPaise: number, currency: string) {
    return new Intl.NumberFormat('en-IN', { style: 'currency', currency }).format(amountPaise / 100);
  }

  private attachable() {
    return this.view()?.documents.length === 0 && !this.pending();
  }

  private restore() {
    this.generation++;
    this.clearPreview();
    this.view.set(null);
    this.pending.set(null);
    this.saved.set(false);
    this.error.set('');
    this.reauthenticate.set(false);
    this.busy.set(false);
    void this.run(scope => this.load(scope));
  }

  private async load(scope: Invocation) {
    const draft = await this.drafts.read(scope);
    if (!scope.current()) return;
    this.revision = draft.revision;
    this.pending.set(draft.pending ?? null);
    const view = await this.api.list(scope);
    if (scope.current()) this.view.set(view);
  }

  private async finish(scope: Invocation, intent: InvoiceIntent, lookup: boolean) {
    try {
      const version = await this.api.finish(scope, intent, lookup);
      if (!scope.current()) return;
      await this.drafts.settle(scope, intent.operationKey);
      if (!scope.current()) return;
      this.pending.set(null);
      this.saved.set(true);
      this.view.update(view => view && { ...view, documents: [version] });
    } catch (error) {
      const code = this.code(error);
      if (!scope.current() || lookup || !code || !definiteRefusals.has(code)) throw error;
      await this.drafts.settle(scope, intent.operationKey);
      if (!scope.current()) return;
      this.revision++;
      this.pending.set(null);
      if (code === 'BillingDocument.AlreadyAttached') {
        const view = await this.api.list(scope);
        if (scope.current()) this.view.set(view);
      }
      throw error;
    }
  }

  private async run(work: (scope: Invocation) => Promise<void>) {
    const admin = this.session.adminId(), session = this.session.sessionId(), account = this.account(), order = this.order();
    if (!admin || !session || !account || !order || !this.allowed() || this.busy()) return;
    const generation = this.generation;
    const current = () => generation === this.generation && this.allowed()
      && this.session.adminId() === admin && this.session.sessionId() === session && this.account() === account && this.order() === order;
    this.busy.set(true);
    this.error.set('');
    this.reauthenticate.set(false);
    try {
      await work({ admin, session, account, order, current });
    } catch (error) {
      if (current()) this.refuse(error);
    } finally {
      if (current()) this.busy.set(false);
    }
  }

  private refuse(error: unknown) {
    const code = this.code(error);
    this.reauthenticate.set(code === 'ManualPayment.RecentMfaRequired' || (error instanceof HttpErrorResponse && error.status === 401));
    if (this.reauthenticate()) this.error.set('Sign in with MFA to continue this upload.');
    else if (this.pending()) this.error.set('The upload is unresolved. Check or retry the saved operation.');
    else if (error instanceof InvoiceInputError) this.error.set(error.message);
    else if (code === 'ManualPayment.AccessDenied') this.error.set('Billing capability is required.');
    else this.error.set(definiteRefusals.get(code ?? '') ?? 'The invoice request could not be completed.');
  }

  private code(error: unknown) {
    return error instanceof HttpErrorResponse ? problemCode(error.error) : null;
  }
}
