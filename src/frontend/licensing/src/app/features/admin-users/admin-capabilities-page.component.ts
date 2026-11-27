import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { problemCode } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import { finalize } from 'rxjs';
import { AdminSessionService } from '../auth/admin-session.service';
import {
  AdminCapabilitiesApi, type AdminAccessReason, type AdminGrant, type AdministratorAccess, adminGrants, manageAdminAccess,
} from './admin-capabilities-api.service';

const refusalText: Record<string, string> = {
  'Admin.LastRecoverableOwner': 'This change would leave no recoverable platform owner.',
  'Admin.OwnerRequiresMfa': 'A platform owner must be active and enrolled in MFA.',
  'Admin.CapabilityUpdateInvalid': 'Select a reason and confirm your changes.',
};

@Component({
  selector: 'adm-admin-capabilities-page',
  imports: [ButtonDirective, RouterLink],
  templateUrl: './admin-capabilities-page.component.html',
  styleUrls: ['../form-page.css', './admin-capabilities-page.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminCapabilitiesPageComponent {
  private readonly api = inject(AdminCapabilitiesApi);
  private readonly session = inject(AdminSessionService);
  private readonly destroy = inject(DestroyRef);
  private readonly cursors = signal<(string | null)[]>([null]);
  protected readonly access = computed(() => this.session.capabilities().includes(manageAdminAccess));
  protected readonly notice = this.session.notice;
  protected readonly grants = Object.entries(adminGrants) as [AdminGrant, string][];
  protected readonly rows = signal<readonly AdministratorAccess[]>([]);
  protected readonly selected = signal<AdministratorAccess | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly saved = signal('');
  protected readonly reloadRequired = signal(false);
  protected readonly reauthenticate = signal(false);
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly hasPrevious = computed(() => this.cursors().length > 1);

  constructor() {
    effect(() => {
      if (this.access()) untracked(() => this.load(null));
      else {
        this.selected.set(null);
        this.rows.set([]);
      }
    });
  }

  protected select(id: string) {
    this.selected.set(this.rows().find((row) => row.id === id) ?? null);
    this.error.set('');
    this.saved.set('');
  }

  protected reload() {
    this.load(this.cursors().at(-1) ?? null);
  }

  protected next() {
    const cursor = this.nextCursor();
    if (!cursor || this.busy()) return;
    this.cursors.update((items) => [...items, cursor]);
    this.load(cursor);
  }

  protected previous() {
    if (!this.hasPrevious() || this.busy()) return;
    this.cursors.update((items) => items.slice(0, -1));
    this.reload();
  }

  protected reconfirm() {
    this.session.check(true).pipe(takeUntilDestroyed(this.destroy)).subscribe();
  }

  protected submit(event: SubmitEvent, form: HTMLFormElement) {
    event.preventDefault();
    const selected = this.selected();
    if (!this.access() || !selected || selected.disabled || this.busy()
      || this.reloadRequired() || this.reauthenticate()) return;

    const data = new FormData(form);
    if (!data.get('reason') || !data.has('confirmed')) return;
    this.busy.set(true);
    this.error.set('');
    this.saved.set('');
    this.api.update(selected.id, {
      capabilities: data.getAll('grant') as AdminGrant[],
      expectedVersion: selected.version,
      reason: data.get('reason') as AdminAccessReason,
      confirmed: true,
      disable: data.has('disable'),
    }).pipe(takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false))).subscribe({
      next: (updated) => this.accept(updated, selected),
      error: (error: HttpErrorResponse) => this.refuse(error),
    });
  }

  private load(cursor: string | null) {
    if (!this.access() || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.saved.set('');
    this.selected.set(null);
    this.reloadRequired.set(false);
    this.api.list(cursor).pipe(takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false))).subscribe({
      next: ({ items, nextCursor }) => {
        this.rows.set(items);
        this.nextCursor.set(nextCursor);
      },
      error: (error: HttpErrorResponse) => {
        this.rows.set([]);
        this.nextCursor.set(null);
        this.refuse(error);
      },
    });
  }

  private accept(updated: AdministratorAccess, prior: AdministratorAccess) {
    this.rows.update((items) => items.map((row) => row.id === updated.id ? updated : row));
    this.selected.set(null);
    this.saved.set(updated.disabled ? 'Administrator disabled.' : 'Administrator access saved.');
    if (updated.isCurrent && updated.version !== prior.version) this.reconfirm();
  }

  private refuse(error: HttpErrorResponse) {
    const code = problemCode(error.error) ?? '';
    if (code === 'Admin.RecentMfaRequired') {
      this.reauthenticate.set(true);
      this.error.set('Sign in again with your authenticator, then submit the change within five minutes.');
    } else if (refusalText[code]) {
      this.error.set(refusalText[code]);
    } else if (error.status === 401 || code === 'Admin.AccessDenied' || (error.status === 403 && !code)) {
      this.selected.set(null);
      this.rows.set([]);
      this.error.set('Administrator access could not be confirmed.');
      this.reconfirm();
    } else {
      this.selected.set(null);
      this.reloadRequired.set(true);
      this.error.set(code === 'Admin.CapabilityVersionConflict'
        ? 'Administrator access changed. Reload before editing.'
        : 'Could not confirm administrator access. Reload before editing.');
    }
  }
}
