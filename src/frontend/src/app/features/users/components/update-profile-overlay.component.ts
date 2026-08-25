import { Component, EventEmitter, Input, Output, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { AuthUser } from '../../../core/auth/auth.models';
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

@Component({
  selector: 'app-update-profile-overlay',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    InputTextModule,
    ButtonModule,
    ProgressSpinnerModule,
    TranslocoPipe,
  ],
  templateUrl: './update-profile-overlay.component.html',
  styleUrl: './update-profile-overlay.component.scss',
})
export class UpdateProfileOverlayComponent {
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);
  private readonly localizationService = inject(LocalizationService);

  readonly isSubmitting = this.store.selectSignal(selectUsersSubmitting);
  readonly serverError = this.store.selectSignal(selectUsersErrorMessage);
  readonly lastMutationType = this.store.selectSignal(selectUsersLastMutationType);
  readonly lastMutationSucceeded = this.store.selectSignal(selectUsersLastMutationSucceeded);
  readonly isUpdateProfilePending = signal(false);

  @Input({ required: true }) user!: AuthUser;
  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    firstName: ['', InputValidators.requiredText(100)],
    lastName: ['', InputValidators.requiredText(100)],
    email: ['', InputValidators.email()],
    phoneNumber: ['', InputValidators.phoneNumber({ required: false, maxLength: 32 })],
  });

  readonly progressSpinnerPt = {
    root: { class: 'update-profile-spinner-root' },
  };

  constructor() {
    effect(() => {
      const isUpdateProfileSuccess =
        this.lastMutationType() === 'update-profile' && this.lastMutationSucceeded();
      if (!this.isUpdateProfilePending() || !isUpdateProfileSuccess || this.isSubmitting()) {
        return;
      }

      this.isUpdateProfilePending.set(false);
      this.store.dispatch(UsersActions.clearMutationStatus());
      this.closeRequested.emit();
    });
  }

  ngOnInit(): void {
    this.store.dispatch(UsersActions.clearError());
    this.store.dispatch(UsersActions.clearMutationStatus());
    this.form.patchValue({
      firstName: this.user.firstName,
      lastName: this.user.lastName,
      email: this.user.email ?? '',
      phoneNumber: this.user.phoneNumber ?? '',
    });
  }

  onClose(): void {
    if (this.isSubmitting()) {
      return;
    }

    this.closeRequested.emit();
  }

  onSubmit(): void {
    if (this.isSubmitting()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const payload = {
      firstName: this.form.controls.firstName.value.trim(),
      lastName: this.form.controls.lastName.value.trim(),
      email: this.form.controls.email.value.trim(),
      phoneNumber: this.toNullable(this.form.controls.phoneNumber.value),
      language: this.user.language ?? this.localizationService.currentLanguage(),
    };

    this.store.dispatch(UsersActions.clearError());
    this.store.dispatch(UsersActions.clearMutationStatus());
    this.isUpdateProfilePending.set(true);
    this.store.dispatch(UsersActions.updateProfileRequested({ payload }));
  }

  private toNullable(value: string): string | null {
    const normalized = value.trim();
    return normalized.length > 0 ? normalized : null;
  }
}
