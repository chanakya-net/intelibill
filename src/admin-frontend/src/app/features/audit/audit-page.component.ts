import { NgTemplateOutlet, TitleCasePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, ChangeDetectionStrategy, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import type { Subscription } from 'rxjs';
import { AdminSessionService } from '../auth/admin-session.service';
import {
  AdminAuditApi, type AuditFilters, type AuditPage, auditActions, identityAuditCapability,
} from './admin-audit-api.service';

@Component({
  selector: 'adm-audit-page',
  imports: [ButtonDirective, NgTemplateOutlet, TitleCasePipe, RouterLink],
  templateUrl: './audit-page.component.html',
  styleUrls: ['../form-page.css', './audit-page.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditPageComponent {
  private readonly api = inject(AdminAuditApi);
  private readonly session = inject(AdminSessionService);
  private readonly destroy = inject(DestroyRef);
  private readonly serverDenied = signal(false);
  private filters: AuditFilters = { pageSize: 25 };
  private cursors: (string | null)[] = [null];
  private request?: Subscription;
  private requestVersion = 0;

  protected readonly mayRead = computed(() => this.session.state() === 'authenticated'
    && this.session.capabilities().includes(identityAuditCapability) && !this.serverDenied());
  protected readonly notice = this.session.notice;
  protected readonly actions = Object.entries(auditActions);
  protected readonly actionTitles = auditActions;
  protected readonly utcTime = (instant: string) => instant.replace('T', ' ').replace(/(?:\+00:00|Z)$/, ' UTC');
  protected readonly page = signal<AuditPage | null>(null);
  protected readonly pageIndex = signal(0);
  protected readonly loading = signal(false);
  protected readonly error = signal('');

  constructor() {
    effect(() => {
      const allowed = this.mayRead();
      untracked(() => {
        if (allowed) this.load(0);
        else this.clear();
      });
    });
    this.destroy.onDestroy(() => this.cancel());
  }

  protected apply(event: Event, action: string, from: string, to: string, pageSize: string) {
    event.preventDefault();
    this.filters = { action, fromUtc: utc(from), toUtc: utc(to), pageSize: Number(pageSize) };
    this.cursors = [null];
    this.load(0);
  }

  protected next() {
    const cursor = this.page()?.nextCursor;
    if (!cursor) return;
    const index = this.pageIndex() + 1;
    this.cursors[index] = cursor;
    this.load(index);
  }

  protected previous() {
    if (this.pageIndex() > 0) this.load(this.pageIndex() - 1);
  }

  protected retry() {
    if (this.mayRead()) this.load(this.pageIndex());
    else this.session.check(true).pipe(takeUntilDestroyed(this.destroy)).subscribe();
  }

  private load(index: number) {
    if (!this.mayRead()) return;
    this.cancel();
    const version = this.requestVersion;
    this.page.set(null);
    this.error.set('');
    this.pageIndex.set(index);
    this.loading.set(true);
    this.request = this.api.read(this.filters, this.cursors[index]).subscribe({
      next: (page) => {
        if (version !== this.requestVersion || !this.mayRead()) return;
        this.page.set(page);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        if (version !== this.requestVersion || !this.mayRead()) return;
        this.loading.set(false);
        if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403))
          this.serverDenied.set(true);
        else this.error.set(error instanceof HttpErrorResponse && error.status === 400
          ? 'Check your filters and apply them again.' : 'Audit history could not be loaded.');
      },
    });
  }

  private clear() {
    this.cancel();
    this.filters = { pageSize: 25 };
    this.cursors = [null];
    this.page.set(null);
    this.pageIndex.set(0);
    this.loading.set(false);
    this.error.set('');
  }

  private cancel() {
    this.requestVersion++;
    this.request?.unsubscribe();
    this.request = undefined;
  }
}

function utc(value: string): string | undefined {
  if (!value) return undefined;
  return (value.length === 16 ? value + ':00' : value) + 'Z';
}
