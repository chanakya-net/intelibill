import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { finalize, switchMap } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { ExternalAuthProvider, ExternalLinks } from '../../../core/auth/auth.models';
import {
  IdentityFlowService,
  IdentityOperation,
  IdentityOutcome,
} from '../../../core/auth/identity-flow.service';
import { identityError } from '../../../core/auth/identity-errors';
import { InputValidators } from '../../../shared/forms/input-validation';
@Component({
  selector: 'app-account-security-page',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    ButtonModule,
    InputTextModule,
    PasswordModule,
  ],
  templateUrl: './account-security-page.component.html',
  styleUrl: './account-security-page.component.scss',
})
export class AccountSecurityPageComponent implements OnInit {
  readonly auth = inject(AuthService);
  private readonly flow = inject(IdentityFlowService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly methods = signal<ExternalLinks | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly operation = signal<IdentityOperation | null>(null);
  readonly providers = [ExternalAuthProvider.Google, ExternalAuthProvider.Facebook];
  readonly emailForm = inject(FormBuilder).nonNullable.group({
    email: ['', InputValidators.email()],
  });
  readonly passwordForm = inject(FormBuilder).nonNullable.group({
    password: ['', InputValidators.requiredText()],
  });
  ngOnInit(): void {
    if (this.route.snapshot.queryParamMap.get('identity') === 'pending')
      this.notice.set('identity.emailPending');
    if (this.route.snapshot.queryParamMap.get('identity') === 'failed')
      this.error.set('identity.reauthFailed');
    if (this.route.snapshot.queryParamMap.get('identity') === 'collision')
      this.error.set('identity.collision');
    this.loadMethods();
  }
  loadMethods(): void {
    this.busy.set(true);
    this.auth
      .getExternalLinks()
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: (methods) => this.methods.set(methods),
        error: (error) => this.error.set(identityError(error)),
      });
  }
  providerKey(provider: ExternalAuthProvider): string {
    return provider === ExternalAuthProvider.Google ? 'auth.google' : 'auth.facebook';
  }
  isLinked(provider: ExternalAuthProvider): boolean {
    return this.methods()?.providers.includes(provider) ?? false;
  }
  chooseEmailChange(): void {
    if (this.emailForm.invalid) {
      this.emailForm.markAllAsTouched();
      return;
    }
    this.choose({ kind: 'email', email: this.emailForm.controls.email.value.trim() });
  }
  chooseLink(provider: ExternalAuthProvider): void {
    this.choose({ kind: 'link', provider });
  }
  chooseUnlink(provider: ExternalAuthProvider): void {
    this.choose({ kind: 'unlink', provider });
  }
  cancel(): void {
    if (!this.busy()) {
      this.operation.set(null);
      this.passwordForm.reset();
    }
  }
  confirmPassword(): void {
    const operation = this.operation();
    if (!operation || this.busy()) return;
    if (this.passwordForm.invalid) {
      this.passwordForm.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set('');
    const password = this.passwordForm.controls.password.value;
    this.passwordForm.reset();
    this.auth
      .reauthenticate(password)
      .pipe(
        switchMap((proof) => this.flow.perform(operation, proof)),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({ next: (result) => this.finish(result), error: (error) => this.fail(error) });
  }
  confirmProvider(provider: ExternalAuthProvider): void {
    const operation = this.operation();
    if (!operation || this.busy() || !this.isLinked(provider)) return;
    this.busy.set(true);
    this.error.set('');
    this.flow
      .startProviderReauth(provider, operation)
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({ next: (result) => this.finish(result), error: (error) => this.fail(error) });
  }
  private choose(operation: IdentityOperation): void {
    if (this.busy()) return;
    this.error.set('');
    this.notice.set('');
    this.passwordForm.reset();
    this.operation.set(operation);
  }
  private finish(result: IdentityOutcome): void {
    if (result === 'security-updated') {
      void this.router.navigateByUrl('/login?identity=updated');
      return;
    }
    if (result === 'email-pending') {
      this.notice.set('identity.emailPending');
      this.operation.set(null);
      this.emailForm.reset();
    }
  }
  private fail(error: Parameters<typeof identityError>[0]): void {
    this.error.set(identityError(error));
    this.operation.set(null);
    this.passwordForm.reset();
  }
}
