import { Component, EventEmitter, OnInit, Output, computed, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { AuthService } from '../../../core/auth/auth.service';
import { RootState } from '../../../core/state/app.state';
import { UsersActions } from '../state/users.actions';
import { selectUsersErrorMessage, selectUsersSubmitting } from '../state/users.selectors';
import { InputValidators } from '../../../shared/forms/input-validation';

/**
 * Invites someone to the active shop. The owner chooses an address and a role and nothing else:
 * the invited person creates or signs in to their own account, chooses their own password, and
 * accepts from there. There is deliberately no password or recovery field on this form.
 */
@Component({
  selector: 'app-add-shop-user-overlay',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    InputTextModule,
    ButtonModule,
    ProgressSpinnerModule,
    TranslocoPipe,
  ],
  templateUrl: './add-shop-user-overlay.component.html',
  styleUrl: './add-shop-user-overlay.component.scss',
})
export class AddShopUserOverlayComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);
  private readonly authService = inject(AuthService);

  readonly isSubmitting = this.store.selectSignal(selectUsersSubmitting);
  readonly serverError = this.store.selectSignal(selectUsersErrorMessage);

  /** Invitations are issued for the shop the owner is currently working in. */
  readonly activeShopId = computed(() => this.authService.session()?.activeShopId ?? null);
  readonly activeShopName = computed(() => {
    const session = this.authService.session();
    return session?.shops.find((shop) => shop.shopId === session.activeShopId)?.shopName ?? '';
  });

  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    email: ['', InputValidators.email()],
    role: ['Manager' as 'Manager' | 'Staff', [Validators.required]],
  });

  ngOnInit(): void {
    this.store.dispatch(UsersActions.clearError());
    this.store.dispatch(UsersActions.clearMutationStatus());
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

    const shopId = this.activeShopId();
    if (!shopId) {
      return;
    }

    this.store.dispatch(UsersActions.clearError());
    this.store.dispatch(UsersActions.clearMutationStatus());

    this.store.dispatch(
      UsersActions.inviteMemberRequested({
        shopId,
        payload: {
          email: this.form.controls.email.value.trim().toLowerCase(),
          role: this.form.controls.role.value,
        },
      }),
    );
  }
}
