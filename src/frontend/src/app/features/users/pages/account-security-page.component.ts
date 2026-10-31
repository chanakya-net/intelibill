import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Store } from '@ngrx/store';
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
import { LocalizationService } from '../../../core/i18n/localization.service';
import { RootState } from '../../../core/state/app.state';
import { UsersActions } from '../state/users.actions';
import {
  selectUsersErrorMessage,
  selectUsersLastMutationSucceeded,
  selectUsersLastMutationType,
  selectUsersSubmitting,
} from '../state/users.selectors';
import { InputValidators } from '../../../shared/forms/input-validation';
/**
 * The whole account on one page: the personal details the person edits themselves, the verified
 * email and sign-in methods behind a fresh password check, and the devices holding a session.
 */
@Component({
  selector: 'app-account-security-page',
  standalone: true,
  imports: [
    DatePipe,
    ReactiveFormsModule,
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
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);
  private readonly localizationService = inject(LocalizationService);
  readonly methods = signal<SignInMethods | null>(null);
  readonly devices = signal<readonly DeviceSession[]>([]);
  readonly devicesBusy = signal(false);
  readonly confirmingEndAll = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly operation = signal<IdentityOperation | null>(null);
  readonly profileSubmitting = this.store.selectSignal(selectUsersSubmitting);
  readonly profileError = this.store.selectSignal(selectUsersErrorMessage);
  readonly profileNotice = signal('');
  private readonly lastMutationType = this.store.selectSignal(selectUsersLastMutationType);
  private readonly lastMutationSucceeded = this.store.selectSignal(
    selectUsersLastMutationSucceeded,
  );
  private readonly profileSavePending = signal(false);
  readonly profileForm = this.formBuilder.nonNullable.group({
    firstName: ['', InputValidators.requiredText(100)],
    lastName: ['', InputValidators.requiredText(100)],
    email: ['', InputValidators.email({ required: false })],
    phoneNumber: ['', InputValidators.phoneNumber({ required: false, maxLength: 32 })],
  });
  readonly passwordForm = this.formBuilder.nonNullable.group({
    password: ['', InputValidators.requiredText()],
  });
  private readonly typedEmail = toSignal(this.profileForm.controls.email.valueChanges, {
    initialValue: this.profileForm.controls.email.value,
  });
  /**
   * A different address in the email field turns the save into an email change, which costs a
   * password check and a verification mail. Anything else saves straight away.
   */
  readonly emailChangeRequested = computed(() => {
    const typed = this.typedEmail().trim();
    const current = this.auth.session()?.user?.email ?? '';
    return typed.length > 0 && typed.toLowerCase() !== current.toLowerCase();
  });
  constructor() {
    effect(() => {
      const saved = this.lastMutationType() === 'update-profile' && this.lastMutationSucceeded();
      if (!this.profileSavePending() || !saved || this.profileSubmitting()) return;
      this.profileSavePending.set(false);
      this.store.dispatch(UsersActions.clearMutationStatus());
      this.profileNotice.set('profile.profileSaved');
    });
  }
  ngOnInit(): void {
    if (this.route.snapshot.queryParamMap.get('identity') === 'pending')
      this.notice.set('identity.emailPending');
    const failureMessage = identityFailureMessage(
      this.route.snapshot.queryParamMap.get('identity'),
    );
    if (failureMessage) this.error.set(failureMessage);
    this.store.dispatch(UsersActions.clearError());
    this.store.dispatch(UsersActions.clearMutationStatus());
    this.fillProfileForm();
    this.loadMethods();
    this.loadDevices();
  }
  /** The page is the only editor of these details, so it starts from whatever the session holds. */
  private fillProfileForm(): void {
    const user = this.auth.session()?.user;
    if (!user) return;
    this.profileForm.patchValue({
      firstName: user.firstName,
      lastName: user.lastName,
      email: user.email ?? '',
      phoneNumber: user.phoneNumber ?? '',
    });
  }
  /**
   * Names and phone number save straight away. A new email never rides along with them: it is kept
   * out of the saved payload and sent through the verified change below, so a typo here can never
   * cost someone their sign-in address.
   */
  saveProfile(): void {
    if (this.profileSubmitting()) return;
    if (this.profileForm.invalid) {
      this.profileForm.markAllAsTouched();
      return;
    }
    const user = this.auth.session()?.user;
    if (!user) return;
    const payload = {
      firstName: this.profileForm.controls.firstName.value.trim(),
      lastName: this.profileForm.controls.lastName.value.trim(),
      email: user.email ?? '',
      phoneNumber: this.toNullable(this.profileForm.controls.phoneNumber.value),
      language: user.language ?? this.localizationService.currentLanguage(),
    };
    this.profileNotice.set('');
    this.store.dispatch(UsersActions.clearError());
    this.store.dispatch(UsersActions.clearMutationStatus());
    this.profileSavePending.set(true);
    this.store.dispatch(UsersActions.updateProfileRequested({ payload }));
    if (this.emailChangeRequested())
      this.choose({ kind: 'email', email: this.profileForm.controls.email.value.trim() });
  }
  private toNullable(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
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
      // The verified address only moves once the link in the mail is opened, so the field goes back
      // to the address that still signs this account in.
      this.profileForm.controls.email.setValue(this.auth.session()?.user?.email ?? '');
    }
  }
  private fail(error: Parameters<typeof identityError>[0]): void {
    this.error.set(identityError(error));
    this.operation.set(null);
    this.passwordForm.reset();
  }
}
