import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { FormSectionStylesComponent } from '@intelibill/ui';
import { ButtonDirective } from 'primeng/button';
import { AdminSessionService } from './features/auth/admin-session.service';

@Component({
  selector: 'adm-root',
  imports: [ButtonDirective, FormSectionStylesComponent, RouterOutlet],
  template: `
    <ib-form-section-styles />
    <header>
      <span>InteliBill</span>
      @if (session.state() === 'authenticated' || session.notice() === 'expired') {
        <button pButton type="button" (click)="session.signOut()">Sign out</button>
      }
    </header>
    <router-outlet />
  `,
  styles: `
    header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 1rem clamp(1rem, 4vw, 2.5rem);
      border-bottom: 1px solid var(--app-sand-300);
      color: var(--app-brown-900);
      font-family: var(--app-font-display);
      font-weight: 800;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  protected readonly session = inject(AdminSessionService);
  private readonly router = inject(Router);

  constructor() {
    effect(() => {
      if (this.session.signOutNotice() && !this.router.url.startsWith('/auth/')) {
        void this.router.navigateByUrl('/auth/sign-in', { replaceUrl: true });
      }
    });
  }
}
