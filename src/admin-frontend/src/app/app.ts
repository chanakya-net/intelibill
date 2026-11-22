import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { FormSectionStylesComponent } from '@intelibill/ui';
import { ButtonDirective } from 'primeng/button';
import { filter, map, startWith } from 'rxjs';
import { AdminSessionService } from './features/auth/admin-session.service';

interface NavItem {
  readonly label: string;
  readonly route: string;
  readonly icon: string;
  readonly exact?: boolean;
}

interface NavGroup {
  readonly heading: string;
  readonly items: readonly NavItem[];
}

const statusReaderGrants = ['support', 'billing', 'licensing-manager'] as const;

@Component({
  selector: 'adm-root',
  imports: [ButtonDirective, FormSectionStylesComponent, RouterLink, RouterLinkActive, RouterOutlet],
  styleUrl: './app.css',
  template: `
    <ib-form-section-styles />
    @if (showConsoleShell()) {
      <div class="adm-shell">
        @if (mobileNavOpen()) {
          <div class="adm-drawer-backdrop" (click)="closeMobileNav()" aria-hidden="true"></div>
        }

        <aside class="adm-sidebar" [class.is-open]="mobileNavOpen()" aria-label="Console sidebar">
          <div class="adm-brand">
            <span class="adm-brand__mark" aria-hidden="true">IB</span>
            <div class="adm-brand__text">
              <span class="adm-brand__eyebrow">Provider Console</span>
              <span class="adm-brand__name">InteliBill</span>
            </div>
            <button
              type="button"
              class="adm-drawer-close"
              aria-label="Close navigation"
              (click)="closeMobileNav()"
            >
              <i class="pi pi-times" aria-hidden="true"></i>
            </button>
          </div>

          <nav class="adm-sidebar__nav" aria-label="Console sections">
            @for (group of navGroups(); track group.heading) {
              <div class="adm-nav-group">
                <span class="adm-nav-group__label">{{ group.heading }}</span>
                @for (item of group.items; track item.route) {
                  <a
                    class="adm-nav-link"
                    [routerLink]="item.route"
                    routerLinkActive="is-active"
                    [routerLinkActiveOptions]="{ exact: !!item.exact }"
                    (click)="closeMobileNav()"
                  >
                    <i class="pi" [class]="item.icon" aria-hidden="true"></i>
                    <span>{{ item.label }}</span>
                  </a>
                }
              </div>
            }
          </nav>

          <div class="adm-sidebar__footer">
            <span class="adm-session-dot" aria-hidden="true"></span>
            <span>{{ capabilitySummary() }}</span>
          </div>
        </aside>

        <div class="adm-shell__body">
          <header class="adm-topbar">
            <div class="adm-topbar__left">
              <button
                type="button"
                class="adm-menu-toggle"
                aria-label="Open navigation"
                [attr.aria-expanded]="mobileNavOpen()"
                (click)="toggleMobileNav()"
              >
                <i class="pi pi-bars" aria-hidden="true"></i>
              </button>
              <span class="adm-topbar__context">
                <i class="pi pi-shield" aria-hidden="true"></i>
                <span>Provider Operations</span>
              </span>
            </div>

            <div class="adm-topbar__right">
              <button
                pButton
                severity="secondary"
                [outlined]="true"
                type="button"
                (click)="session.signOut()"
              >
                <i class="pi pi-sign-out" aria-hidden="true"></i>Sign out
              </button>
            </div>
          </header>

          <div class="adm-shell__content">
            <router-outlet />
          </div>
        </div>
      </div>
    } @else {
      <div class="adm-auth-shell">
        <header class="adm-auth-topbar">
          <div class="adm-auth-brand">
            <span class="adm-brand__mark" aria-hidden="true">IB</span>
            <div class="adm-brand__text">
              <span class="adm-brand__eyebrow">Provider Console</span>
              <span class="adm-brand__name">InteliBill</span>
            </div>
          </div>
          @if (session.state() === 'authenticated' || session.notice() === 'expired') {
            <button
              pButton
              severity="secondary"
              [outlined]="true"
              type="button"
              (click)="session.signOut()"
            >
              <i class="pi pi-sign-out" aria-hidden="true"></i>Sign out
            </button>
          }
        </header>
        <div class="adm-auth-stage">
          <router-outlet />
        </div>
      </div>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  protected readonly session = inject(AdminSessionService);
  private readonly router = inject(Router);
  protected readonly mobileNavOpen = signal(false);

  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
      startWith(this.router.url),
    ),
    { initialValue: this.router.url },
  );

  protected readonly showConsoleShell = computed(() => {
    if (this.session.state() !== 'authenticated') return false;
    return !this.currentUrl().startsWith('/auth/');
  });

  protected readonly capabilitySummary = computed(() => {
    const count = this.session.capabilities().length;
    if (count === 0) return 'Session active · No grants';
    return count === 1 ? 'Session active · 1 grant' : `Session active · ${count} grants`;
  });

  protected readonly navGroups = computed<readonly NavGroup[]>(() => {
    const caps = this.session.capabilities();
    const has = (grant: string) => caps.includes(grant);

    const workspace: NavItem[] = [
      { label: 'Overview', route: '/', icon: 'pi-home', exact: true },
      { label: 'Sessions', route: '/security/sessions', icon: 'pi-desktop' },
    ];

    const operations: NavItem[] = [];
    if (statusReaderGrants.some(has)) {
      operations.push({ label: 'Accounts', route: '/accounts', icon: 'pi-building' });
    }
    if (has('security-administrator') || has('support')) {
      operations.push({ label: 'Restrictions', route: '/restrictions', icon: 'pi-ban', exact: true });
    }
    if (has('licensing-manager')) {
      operations.push({ label: 'Reopening', route: '/restrictions/reopening', icon: 'pi-refresh' });
    }

    const commercial: NavItem[] = [];
    if (has('billing')) {
      commercial.push({ label: 'Payments', route: '/payments', icon: 'pi-wallet', exact: true });
      commercial.push({ label: 'Funds Review', route: '/payments/review', icon: 'pi-check-circle' });
    }
    if (has('licensing-manager')) {
      commercial.push({ label: 'Catalogue', route: '/catalogue', icon: 'pi-tags' });
    }

    const governance: NavItem[] = [];
    if (has('admin.access.manage')) {
      governance.push({ label: 'Admin Access', route: '/admin-users', icon: 'pi-users', exact: true });
    }
    if (has('admin.invitations.manage')) {
      governance.push({ label: 'Invitations', route: '/admin-users/invite', icon: 'pi-envelope' });
    }
    if (has('admin.recovery.review')) {
      governance.push({ label: 'Recovery', route: '/security/recovery', icon: 'pi-key' });
    }
    if (has('admin.audit.identity.read')) {
      governance.push({ label: 'Audit Log', route: '/audit', icon: 'pi-history' });
    }

    const groups: NavGroup[] = [{ heading: 'Workspace', items: workspace }];
    if (operations.length) groups.push({ heading: 'Operations', items: operations });
    if (commercial.length) groups.push({ heading: 'Billing & Plans', items: commercial });
    if (governance.length) groups.push({ heading: 'Governance', items: governance });
    return groups;
  });

  constructor() {
    effect(() => {
      if (this.session.signOutNotice() && !this.router.url.startsWith('/auth/')) {
        void this.router.navigateByUrl('/auth/sign-in', { replaceUrl: true });
      }
    });
    effect(() => {
      this.currentUrl();
      this.mobileNavOpen.set(false);
    });
  }

  protected toggleMobileNav() {
    this.mobileNavOpen.update((open) => !open);
  }

  protected closeMobileNav() {
    this.mobileNavOpen.set(false);
  }
}
