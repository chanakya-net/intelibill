import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SESSION_PORT } from '@intelibill/api';

const invitationCapability = 'admin.invitations.manage';

@Component({
  selector: 'adm-landing-page',
  imports: [RouterLink],
  templateUrl: './landing-page.component.html',
  styleUrl: './landing-page.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LandingPageComponent {
  private readonly session = inject(SESSION_PORT);
  protected readonly authenticated = computed(() => this.session.state() === 'authenticated');
  protected readonly capabilities = this.session.capabilities;
  protected readonly invitationCapability = invitationCapability;
}
