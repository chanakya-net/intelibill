import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { SESSION_PORT } from '@intelibill/api';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { firstValueFrom } from 'rxjs';
import { ADMIN_NOTIFICATION_LANGUAGES } from '../../core/i18n/admin-languages';
import {
  NotificationPreviewApi,
  type ProviderNotificationItem,
  formatIstNoticeInstant,
  formatUtcExact,
  notificationReaders,
} from './notification-preview-api.service';

type Outcome = 'idle' | 'loading' | 'denied' | 'missing' | 'unavailable' | 'session';

/**
 * Read-only preview of persisted scheduled licence notification jobs for one billing account.
 * Never writes or claims delivery. Clears immediately on account, capability, or session change
 * and drops late answers from earlier requests.
 */
@Component({
  selector: 'adm-notification-preview',
  standalone: true,
  imports: [TranslocoPipe],
  templateUrl: './notification-preview.component.html',
  styleUrl: './notification-preview.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(window:focus)': 'revalidate()',
    '(document:visibilitychange)': 'revalidate()',
  },
})
export class NotificationPreviewComponent {
  readonly account = input.required<string>();

  private readonly api = inject(NotificationPreviewApi);
  private readonly session = inject(SESSION_PORT);
  private readonly transloco = inject(TranslocoService);

  protected readonly languages = ADMIN_NOTIFICATION_LANGUAGES;
  protected readonly activeLanguage = signal(this.transloco.getActiveLang() || 'en-IN');

  protected readonly mayRead = computed(
    () =>
      this.session.state() === 'authenticated' &&
      this.session.capabilities().some((grant) => (notificationReaders as readonly string[]).includes(grant)),
  );
  protected readonly capabilityKey = computed(() =>
    [...this.session.capabilities()].sort().join(','),
  );

  protected readonly items = signal<readonly ProviderNotificationItem[]>([]);
  protected readonly evaluatedAt = signal<string | null>(null);
  protected readonly commercialIncluded = signal(false);
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly outcome = signal<Outcome>('idle');
  protected readonly loadingMore = signal(false);

  private generation = 0;
  private inFlight = false;

  constructor() {
    effect(() => {
      const account = this.account().trim().toLowerCase();
      const allowed = this.mayRead();
      this.capabilityKey();
      untracked(() => this.resetAndLoad(account, allowed));
    });

    inject(DestroyRef).onDestroy(() => {
      this.generation++;
    });
  }

  protected refresh(): void {
    const account = this.account().trim().toLowerCase();
    if (!account || !this.mayRead()) {
      return;
    }

    this.resetAndLoad(account, true);
  }

  protected loadMore(): void {
    const cursor = this.nextCursor();
    const account = this.account().trim().toLowerCase();
    if (!cursor || !account || !this.mayRead() || this.loadingMore()) {
      return;
    }

    void this.fetchPage(account, cursor, this.generation, true);
  }

  protected revalidate(): void {
    if (this.inFlight || document.visibilityState !== 'visible') {
      return;
    }

    const account = this.account().trim().toLowerCase();
    if (account && this.mayRead()) {
      void this.fetchPage(account, null, this.generation, false);
    }
  }

  protected onLanguageChange(event: Event): void {
    const lang = (event.target as HTMLSelectElement).value;
    this.activeLanguage.set(lang);
    this.transloco.setActiveLang(lang);
  }

  protected utc(value: string | null): string {
    return formatUtcExact(value) ?? '';
  }

  protected ist(value: string | null): string {
    return formatIstNoticeInstant(value) ?? '';
  }

  protected money(paise: number | null, currency: string | null): string {
    if (paise === null) {
      return '';
    }

    return new Intl.NumberFormat(this.activeLanguage(), {
      style: 'currency',
      currency: currency || 'INR',
    }).format(paise / 100);
  }

  protected purposeKey(value: string): string {
    return `notifications.purposes.${value}`;
  }

  protected thresholdKey(value: string): string {
    return `notifications.thresholds.${value}`;
  }

  protected audienceKey(value: string): string {
    return `notifications.audiences.${value}`;
  }

  protected channelKey(value: string): string {
    return `notifications.channels.${value}`;
  }

  protected guidanceKey(value: string): string {
    return `notifications.guidance.${value}`;
  }

  private resetAndLoad(account: string, allowed: boolean): void {
    this.generation++;
    this.inFlight = false;
    this.items.set([]);
    this.evaluatedAt.set(null);
    this.commercialIncluded.set(false);
    this.nextCursor.set(null);
    this.loadingMore.set(false);
    this.outcome.set(allowed && account ? 'loading' : 'idle');

    if (allowed && account) {
      void this.fetchPage(account, null, this.generation, false);
    }
  }

  private async fetchPage(
    account: string,
    after: string | null,
    generation: number,
    append: boolean,
  ): Promise<void> {
    this.inFlight = true;
    if (append) {
      this.loadingMore.set(true);
    } else if (this.items().length === 0) {
      this.outcome.set('loading');
    }

    try {
      const page = await firstValueFrom(this.api.read(account, after));
      if (generation !== this.generation || page.accountId.toLowerCase() !== account) {
        return;
      }

      this.evaluatedAt.set(page.evaluatedAt);
      this.commercialIncluded.set(page.commercialIncluded);
      this.nextCursor.set(page.nextCursor);
      this.items.set(append ? [...this.items(), ...page.items] : page.items);
      this.outcome.set('idle');
    } catch (error: unknown) {
      if (generation !== this.generation) {
        return;
      }

      const code = error instanceof HttpErrorResponse ? error.status : 0;
      this.items.set([]);
      this.nextCursor.set(null);
      this.commercialIncluded.set(false);
      this.outcome.set(
        code === 401 ? 'session' : code === 403 ? 'denied' : code === 404 ? 'missing' : 'unavailable',
      );
    } finally {
      if (generation === this.generation) {
        this.inFlight = false;
        this.loadingMore.set(false);
      }
    }
  }
}
