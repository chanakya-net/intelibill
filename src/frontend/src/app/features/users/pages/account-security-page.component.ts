import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { PasswordModule } from 'primeng/password';
import { finalize, switchMap } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { DeviceSession, SignInMethods } from '../../../core/auth/auth.models';
import { SessionService } from '../services/session.service';
import {
  IdentityFlowService,
  IdentityOperation,
  IdentityOutcome,
} from '../../../core/auth/identity-flow.service';
import { identityError, identityFailureMessage } from '../../../core/auth/identity-errors';
import { InputValidators } from '../../../shared/forms/input-validation';
@Component({
  selector: 'app-account-security-page',
  standalone: true,
  imports: [
    DatePipe,
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
  private readonly sessions = inject(SessionService);
  readonly methods = signal<SignInMethods | null>(null);
  readonly devices = signal<readonly DeviceSession[]>([]);
  readonly devicesBusy = signal(false);
  readonly confirmingEndAll = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly operation = signal<IdentityOperation | null>(null);
  readonly emailForm = inject(FormBuilder).nonNullable.group({
    email: ['', InputValidators.email()],
  });
  readonly passwordForm = inject(FormBuilder).nonNullable.group({
    password: ['', InputValidators.requiredText()],
  });
  ngOnInit(): void {
    if (this.route.snapshot.queryParamMap.get('identity') === 'pending')
      this.notice.set('identity.emailPending');
    const failureMessage = identityFailureMessage(
      this.route.snapshot.queryParamMap.get('identity'),
    );
    if (failureMessage) this.error.set(failureMessage);
    this.loadMethods();
    this.loadDevices();
  }
  loadDevices(): void {
    this.devicesBusy.set(true);
    this.sessions
      .getMySessions()
      .pipe(finalize(() => this.devicesBusy.set(false)))
      .subscribe({
        next: (devices) => this.devices.set(devices),
        error: (error) => this.error.set(identityError(error)),
      });
  }
  /**
   * Ending the device you are on signs this browser out, which is what someone at a shared terminal
   * is reaching for. Any other device just disappears from the list.
   */
  endDevice(device: DeviceSession): void {
    if (this.devicesBusy()) return;
    this.devicesBusy.set(true);
    this.error.set('');
    this.sessions
      .revokeSession(device.sessionId)
      .pipe(finalize(() => this.devicesBusy.set(false)))
      .subscribe({
        next: () => (device.isCurrent ? this.signOutHere() : this.loadDevices()),
        error: (error) => this.error.set(identityError(error)),
      });
  }
  /** Asks first: this is the one action that also ends the device the person is standing at. */
  askEndAllDevices(): void {
    if (!this.devicesBusy()) this.confirmingEndAll.set(true);
  }
  cancelEndAllDevices(): void {
    if (!this.devicesBusy()) this.confirmingEndAll.set(false);
  }
  endAllDevices(): void {
    if (this.devicesBusy()) return;
    this.confirmingEndAll.set(false);
    this.devicesBusy.set(true);
    this.error.set('');
    this.sessions
      .revokeAllSessions()
      .pipe(finalize(() => this.devicesBusy.set(false)))
      .subscribe({
        next: () => this.signOutHere(),
        error: (error) => this.error.set(identityError(error)),
      });
  }
  /**
   * The server has already ended this session, so there is no token left to revoke: clear what is
   * stored locally and send the person to sign in again. Unsynced invoices are kept, because they
   * are the shop's work and not part of the session.
   */
  private signOutHere(): void {
    this.auth.clearSession();
    void this.router.navigateByUrl('/login');
  }
  loadMethods(): void {
    this.busy.set(true);
    this.auth
      .getSignInMethods()
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: (methods) => this.methods.set(methods),
        error: (error) => this.error.set(identityError(error)),
      });
  }
  chooseEmailChange(): void {
    if (this.emailForm.invalid) {
      this.emailForm.markAllAsTouched();
      return;
    }
    this.choose({ kind: 'email', email: this.emailForm.controls.email.value.trim() });
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
  private choose(operation: IdentityOperation): void {
    if (this.busy()) return;
    this.error.set('');
    this.notice.set('');
    this.passwordForm.reset();
    this.operation.set(operation);
  }
  private finish(result: IdentityOutcome): void {
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
