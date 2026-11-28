import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { LocaleMoneyPipe } from '../../../shared/pipes/locale-money.pipe';
import { formatBillingInstant } from './subscription-status.models';
import type {
  AccountLicenseNoticeItem,
  LicenseNoticeFailure,
  ShopLicenseNoticeItem,
} from './license-notice.models';
import { LicenseNoticeReadError, LicenseNoticeService } from './license-notice.service';

type NoticeView =
  | { readonly kind: 'idle' | 'loading' }
  | { readonly kind: 'failed'; readonly failure: LicenseNoticeFailure }
  | { readonly kind: 'shop'; readonly notices: readonly ShopLicenseNoticeItem[]; readonly nextDueAt: string | null }
  | {
      readonly kind: 'account';
      readonly evaluatedAt: string;
      readonly notices: readonly AccountLicenseNoticeItem[];
      readonly nextDueAt: string | null;
    };

const MIN_REFRESH_MS = 1_000;
const MAX_REFRESH_MS = 24 * 60 * 60 * 1_000;

/**
 * Displays due operational shop notices (shell banner) or due commercial account notices (selected account).
 * Clears immediately on scope/account/session transitions, drops late epoch replies, and revalidates on
 * focus, visibility, reconnect, and the server-reported next due boundary.
 */
@Component({
  selector: 'app-license-notice',
  standalone: true,
  imports: [TranslocoPipe, LocaleMoneyPipe],
  templateUrl: './license-notice.component.html',
  styleUrl: './license-notice.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(window:focus)': 'revalidate()',
    '(window:online)': 'revalidate()',
    '(document:visibilitychange)': 'revalidate()',
  },
})
export class LicenseNoticeComponent {
  private readonly api = inject(LicenseNoticeService);
  private readonly auth = inject(AuthService);
  private readonly shopContext = inject(ShopContextService);
  private readonly localization = inject(LocalizationService);

  readonly mode = input<'shop' | 'account'>('shop');
  readonly accountId = input<string | null>(null);

  protected readonly view = signal<NoticeView>({ kind: 'idle' });

  private readonly scope = computed(() => {
    const session = this.auth.session();
    if (!session || this.shopContext.transitioning()) {
      return null;
    }

    const mode = this.mode();
    if (mode === 'account') {
      const account = this.accountId()?.trim().toLowerCase() || null;
      return account ? { mode, userId: session.user.id, targetId: account } : null;
    }

    if (this.shopContext.dataRestricted()) {
      return null;
    }

    const shopId = session.activeShopId;
    return shopId ? { mode, userId: session.user.id, targetId: shopId } : null;
  });

  private generation = 0;
  private inFlight = false;
  private nextTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    effect(() => {
      const current = this.scope();
      untracked(() => this.resetAndLoad(current));
    });

    inject(DestroyRef).onDestroy(() => {
      this.generation++;
      this.clearTimer();
    });
  }

  protected revalidate(): void {
    if (this.inFlight || document.visibilityState !== 'visible') {
      return;
    }

    const current = this.scope();
    if (current) {
      void this.fetchNotices(current, this.generation);
    }
  }

  protected instant(value: string | null): string | null {
    return formatBillingInstant(value, this.localization.currentLanguage(), true);
  }

  protected rupees(paise: number | null): number | null {
    return paise === null ? null : paise / 100;
  }

  protected guidanceKey(code: string): string {
    return `billing.notices.guidance.${code}`;
  }

  protected purposeKey(purpose: string): string {
    return `billing.notices.purposes.${purpose}`;
  }

  protected thresholdKey(threshold: string): string {
    return `billing.notices.thresholds.${threshold}`;
  }

  private resetAndLoad(
    current: { readonly mode: 'shop' | 'account'; readonly userId: string; readonly targetId: string } | null,
  ): void {
    this.generation++;
    this.inFlight = false;
    this.clearTimer();
    this.view.set({ kind: 'idle' });
    if (!current) {
      return;
    }

    if (current.mode === 'account') {
      this.view.set({ kind: 'loading' });
    }

    void this.fetchNotices(current, this.generation);
  }

  private async fetchNotices(
    target: { readonly mode: 'shop' | 'account'; readonly userId: string; readonly targetId: string },
    generation: number,
  ): Promise<void> {
    this.inFlight = true;
    try {
      if (target.mode === 'shop') {
        const result = await firstValueFrom(this.api.shop(target.targetId));
        if (generation !== this.generation || result.shopId !== target.targetId) {
          return;
        }

        this.view.set({ kind: 'shop', notices: result.notices, nextDueAt: result.nextDueAt });
        this.scheduleNextRefresh(result.nextDueAt, generation);
        return;
      }

      const result = await firstValueFrom(this.api.account(target.targetId));
      if (generation !== this.generation || result.accountId.toLowerCase() !== target.targetId) {
        return;
      }

      this.view.set({
        kind: 'account',
        evaluatedAt: result.evaluatedAt,
        notices: result.notices,
        nextDueAt: result.nextDueAt,
      });
      this.scheduleNextRefresh(result.nextDueAt, generation);
    } catch (error: unknown) {
      if (generation !== this.generation) {
        return;
      }

      const failure: LicenseNoticeFailure = error instanceof LicenseNoticeReadError ? error.failure : 'unavailable';
      this.view.set(target.mode === 'account' ? { kind: 'failed', failure } : { kind: 'idle' });
    } finally {
      if (generation === this.generation) {
        this.inFlight = false;
      }
    }
  }

  private scheduleNextRefresh(nextDueAt: string | null, generation: number): void {
    this.clearTimer();
    if (!nextDueAt) {
      return;
    }

    const dueMs = Date.parse(nextDueAt);
    if (!Number.isFinite(dueMs)) {
      return;
    }

    const delay = dueMs - Date.now();
    if (delay < 0 || delay > MAX_REFRESH_MS) {
      return;
    }

    this.nextTimer = setTimeout(() => {
      if (generation !== this.generation || this.inFlight) {
        return;
      }

      const current = this.scope();
      if (current) {
        void this.fetchNotices(current, generation);
      }
    }, Math.max(MIN_REFRESH_MS, delay + 250));
  }

  private clearTimer(): void {
    if (this.nextTimer !== null) {
      clearTimeout(this.nextTimer);
      this.nextTimer = null;
    }
  }
}
