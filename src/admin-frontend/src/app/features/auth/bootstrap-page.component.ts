import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { problemCode } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import { finalize } from 'rxjs';
import { BootstrapApi } from './bootstrap-api.service';

type View = 'loading' | 'setup' | 'pending' | 'resume' | 'closed';

const messages: Readonly<Record<string, string>> = {
  'Bootstrap.InvalidSetup': 'Check your setup proof and password, then try again.',
  'Bootstrap.ResumeRefused': 'Check your verified email and password, then try again.',
  'Bootstrap.Throttled': 'Too many attempts. Try again later.',
  'Bootstrap.Unavailable': 'First owner setup is closed or the proof has expired.',
  'Bootstrap.RequestRejected': 'This setup request expired. Reload the page and try again.',
};

@Component({
  selector: 'adm-bootstrap-page',
  imports: [ButtonDirective, RouterLink],
  templateUrl: './bootstrap-page.component.html',
  styleUrl: '../form-page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BootstrapPageComponent {
  private readonly api = inject(BootstrapApi);
  private readonly router = inject(Router);
  private readonly destroy = inject(DestroyRef);
  private requestToken?: string;

  protected readonly view = signal<View>('loading');
  protected readonly error = signal('');
  protected readonly busy = signal(false);

  constructor() {
    if (inject(ActivatedRoute).snapshot.data['enrollment']) {
      this.readEnrollment();
    } else {
      this.loadContext();
    }
  }

  protected submit(event: SubmitEvent, identifier: HTMLInputElement, password: HTMLInputElement) {
    event.preventDefault();
    if (this.busy() || !this.requestToken) return;

    if (new TextEncoder().encode(password.value).length > 72) {
      this.error.set('Use a password of at most 72 UTF-8 bytes.');
      return;
    }

    const resume = this.view() === 'resume';
    const request = resume
      ? this.api.resume(this.requestToken, password.value, identifier.value)
      : this.api.submit(this.requestToken, password.value, identifier.value.trim());
    password.value = '';
    if (!resume) identifier.value = '';
    this.busy.set(true);
    this.error.set('');

    request.pipe(takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false))).subscribe({
      next: () => {
        if (resume) this.readEnrollment();
        else void this.router.navigateByUrl('/auth/bootstrap/enrollment');
      },
      error: (error: HttpErrorResponse) => {
        this.error.set(messages[problemCode(error.error) ?? ''] ?? 'Setup could not be completed. Try again.');
      },
    });
  }

  private readEnrollment() {
    this.api.enrollment().pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: () => this.view.set('pending'),
      error: (error: HttpErrorResponse) => {
        if (error.status === 401) this.loadContext(true);
        else this.reportUnavailable();
      },
    });
  }

  private loadContext(enrollment = false) {
    this.api.context().pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: (context) => {
        this.requestToken = context.requestToken;
        if (context.canResume && !enrollment) {
          void this.router.navigateByUrl('/auth/bootstrap/enrollment', { replaceUrl: true });
          return;
        }
        this.view.set(this.getView(enrollment, context.available, context.canResume));
      },
      error: () => this.reportUnavailable(),
    });
  }

  private getView(enrollment: boolean, available: boolean, canResume: boolean): View {
    if (enrollment && canResume) return 'resume';
    if (!enrollment && available) return 'setup';
    return 'closed';
  }

  private reportUnavailable() {
    this.view.set('closed');
    this.error.set('Provider setup is temporarily unavailable. Reload the page to try again.');
  }
}
