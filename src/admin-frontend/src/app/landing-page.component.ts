import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { SESSION_PORT, type ProblemDetails } from '@intelibill/api';
import { ConfirmationDialogComponent } from '@intelibill/ui/confirmation-dialog';
import { ConfirmationService } from 'primeng/api';
import { ButtonDirective } from 'primeng/button';

@Component({
  selector: 'adm-landing-page',
  imports: [ButtonDirective, ConfirmationDialogComponent],
  providers: [ConfirmationService],
  templateUrl: './landing-page.component.html',
  styleUrl: './landing-page.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LandingPageComponent {
  private readonly session = inject(SESSION_PORT);
  private readonly confirmation = inject(ConfirmationService);

  protected readonly accessProblem = computed<ProblemDetails>(() => ({
    title:
      this.session.state() === 'unauthenticated'
        ? 'No provider session'
        : 'Provider access unavailable',
  }));
  protected readonly hasPermissions = computed(
    () => this.session.state() === 'authenticated' && this.session.capabilities().length > 0,
  );
  protected readonly showHelp = signal(true);

  protected toggleHelp(event: MouseEvent): void {
    if (!this.showHelp()) {
      this.showHelp.set(true);
      return;
    }

    this.confirmation.confirm({
      target: event.currentTarget as EventTarget,
      header: 'Hide access help?',
      message: 'You can show it again from this page.',
      acceptLabel: 'Hide help',
      rejectLabel: 'Cancel',
      defaultFocus: 'reject',
      accept: () => this.showHelp.set(false),
    });
  }
}
