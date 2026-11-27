import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import type { Subscription } from 'rxjs';
import { AdminSessionService } from '../../auth/admin-session.service';
import {
  AdminDeviceSessionsApi, type AdminDevicePage, type AdminDeviceSession, type RevokeReason,
} from './admin-device-sessions-api.service';

@Component({
  selector: 'adm-sessions-page',
  imports: [ButtonDirective, DatePipe, RouterLink],
  templateUrl: './sessions-page.component.html',
  styleUrl: '../../form-page.css',
  styles: `
    dl { display: grid; gap: .25rem .75rem; grid-template-columns: max-content 1fr; }
    dd { margin: 0; }
    form > div, nav { display: flex; flex-wrap: wrap; gap: .5rem; }
    select { min-height: 2.75rem; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SessionsPageComponent {
  protected readonly session = inject(AdminSessionService);
  private readonly api = inject(AdminDeviceSessionsApi);
  private readonly destroy = inject(DestroyRef);
  private cursors: (string | null)[] = [null];
  private reading?: Subscription;
  private revoking?: Subscription;

  protected readonly index = signal(0);
  protected readonly page = signal<AdminDevicePage | null>(null);
  protected readonly selection = signal<AdminDeviceSession | null>(null);
  protected readonly error = signal('');
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly status = signal('');

  constructor() {
    effect(() => {
      const owner = this.session.sessionId();
      untracked(() => {
        this.clear();
        this.status.set('');
        if (owner) this.load(0);
      });
    });
    this.destroy.onDestroy(() => this.clear());
  }

  protected next() {
    const cursor = this.page()?.nextCursor;
    if (!cursor) return;
    const index = this.index() + 1;
    this.cursors[index] = cursor;
    this.load(index);
  }

  protected load(index: number) {
    const owner = this.session.sessionId();
    if (!owner) return;
    this.reading?.unsubscribe();
    this.index.set(index);
    this.selection.set(null);
    this.error.set('');
    this.page.set(null);
    this.loading.set(true);
    this.reading = this.api.list(this.cursors[index]).subscribe({
      next: (page) => {
        if (this.session.sessionId() !== owner) return;
        this.page.set(page);
        this.loading.set(false);
      },
      error: () => {
        if (this.session.sessionId() !== owner) return;
        this.loading.set(false);
        this.error.set('Device sessions could not be loaded.');
      },
    });
  }

  protected select(device: AdminDeviceSession) {
    this.error.set('');
    this.status.set('');
    this.selection.set(device);
  }

  protected revoke(event: SubmitEvent, reason: string) {
    event.preventDefault();
    const owner = this.session.sessionId();
    const target = this.selection();
    if (!owner || !target || this.saving() || target.id === owner || !isRevokeReason(reason)) return;
    this.saving.set(true);
    this.error.set('');
    this.revoking = this.api.revoke(target.id, reason).subscribe({
      next: () => {
        if (this.session.sessionId() !== owner) return;
        this.saving.set(false);
        this.status.set('Device session revoked.');
        this.cursors = [null];
        this.load(0);
      },
      error: (error: unknown) => {
        if (this.session.sessionId() !== owner) return;
        this.saving.set(false);
        this.error.set(revokeError(error));
      },
    });
  }

  protected resume() {
    this.session.check(true).pipe(takeUntilDestroyed(this.destroy)).subscribe();
  }

  private clear() {
    this.reading?.unsubscribe();
    this.revoking?.unsubscribe();
    this.cursors = [null];
    this.page.set(null);
    this.selection.set(null);
    this.error.set('');
    this.loading.set(false);
    this.saving.set(false);
  }
}

function isRevokeReason(reason: string): reason is RevokeReason {
  return reason === 'device-unused' || reason === 'device-unrecognized';
}

function revokeError(error: unknown): string {
  if (error instanceof HttpErrorResponse && error.error?.code === 'Admin.RecentMfaRequired')
    return 'Sign in again with your authenticator to revoke another device.';
  return 'Device revocation could not be confirmed. Try again.';
}
