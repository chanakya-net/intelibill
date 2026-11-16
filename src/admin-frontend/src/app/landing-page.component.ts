import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { SESSION_PORT } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import { adminGrants, manageAdminAccess, type AdminGrant } from './features/admin-users/admin-capabilities-api.service';
import { statusReaders } from './features/accounts/account-status-api.service';
import { identityAuditCapability } from './features/audit/admin-audit-api.service';
import { licensingManager } from './features/catalogue/catalogue-api.service';
import { AdminSessionService } from './features/auth/admin-session.service';

const invitationCapability = 'admin.invitations.manage';
const recoveryCapability = 'admin.recovery.review';

@Component({
  selector: 'adm-landing-page',
  imports: [ButtonDirective, RouterLink],
  templateUrl: './landing-page.component.html',
  styleUrl: './landing-page.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LandingPageComponent {
  private readonly session = inject(SESSION_PORT);
  private readonly admin = inject(AdminSessionService);
  private readonly destroy = inject(DestroyRef);
  protected readonly authenticated = computed(() => this.session.state() === 'authenticated');
  protected readonly notice = this.admin.notice;
  protected readonly capabilities = this.session.capabilities;
  protected readonly invitationCapability = invitationCapability;
  protected readonly auditCapability = identityAuditCapability;
  protected readonly recoveryCapability = recoveryCapability;
  protected readonly accessManagementCapability = manageAdminAccess;
  protected readonly licensingManager = licensingManager;
  protected readonly canReadStatus = computed(() =>
    this.capabilities().some(grant => (statusReaders as readonly string[]).includes(grant)));

  protected capabilityLabel(capability: string) {
    return adminGrants[capability as AdminGrant] ?? capability;
  }

  /** A user action, so renewing here counts as activity; nothing renews in the background. */
  protected resume() {
    this.admin.check(true).pipe(takeUntilDestroyed(this.destroy)).subscribe();
  }
}
