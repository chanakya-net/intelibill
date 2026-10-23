import {
  Component,
  EventEmitter,
  Input,
  OnChanges,
  OnInit,
  Output,
  SimpleChanges,
  computed,
  inject,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Store } from '@ngrx/store';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { AuthService } from '../../../core/auth/auth.service';
import { RootState } from '../../../core/state/app.state';
import { MembershipRole, MembershipStatus, ShopUser } from '../services/user-account.service';
import { UsersActions } from '../state/users.actions';
import { selectUsersErrorMessage, selectUsersSubmitting } from '../state/users.selectors';

/**
 * Edits one membership in the active shop: role and status, nothing else.
 *
 * The earlier version of this form submitted the member's global email, phone, names and
 * login-enabled flag, which let the owner of one shop take over an account that owned another.
 * Those controls are gone — the account holder changes their own sign-in details — and the
 * membership version read with the row travels back with the change so a stale screen cannot
 * overwrite a newer decision.
 */
@Component({
  selector: 'app-edit-shop-user-overlay',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    InputTextModule,
    ButtonModule,
    ProgressSpinnerModule,
    TranslocoPipe,
  ],
  templateUrl: './edit-shop-user-overlay.component.html',
  styleUrl: './edit-shop-user-overlay.component.scss',
})
export class EditShopUserOverlayComponent implements OnInit, OnChanges {
  private readonly formBuilder = inject(FormBuilder);
  private readonly store = inject(Store<RootState>);
  private readonly authService = inject(AuthService);

  readonly isSubmitting = this.store.selectSignal(selectUsersSubmitting);
  readonly serverError = this.store.selectSignal(selectUsersErrorMessage);

  readonly activeShopId = computed(() => this.authService.session()?.activeShopId ?? null);
  readonly activeShopName = computed(() => {
    const session = this.authService.session();
    return session?.shops.find((shop) => shop.shopId === session.activeShopId)?.shopName ?? '';
  });
  readonly memberName = computed(() => `${this.user?.firstName ?? ''} ${this.user?.lastName ?? ''}`.trim());

  @Input({ required: true }) user!: ShopUser;
  @Output() readonly closeRequested = new EventEmitter<void>();

  readonly form = this.formBuilder.nonNullable.group({
    role: ['Manager' as MembershipRole, [Validators.required]],
    status: ['Active' as MembershipStatus, [Validators.required]],
  });

  ngOnInit(): void {
    this.patchFormFromUser();
    this.store.dispatch(UsersActions.clearError());
    this.store.dispatch(UsersActions.clearMutationStatus());
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['user']) {
      return;
    }

    this.patchFormFromUser();
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
      UsersActions.updateMembershipRequested({
        shopId,
        userId: this.user.userId,
        payload: {
          role: this.form.controls.role.value,
          status: this.form.controls.status.value,
          expectedVersion: this.user.membershipVersion ?? 0,
        },
      }),
    );
  }

  private patchFormFromUser(): void {
    if (!this.user) {
      return;
    }

    this.form.patchValue({
      role: this.user.role.trim().toLowerCase() === 'manager' ? 'Manager' : 'Staff',
      status: normalizeStatus(this.user.status),
    });
  }
}

function normalizeStatus(status: string | undefined): MembershipStatus {
  switch ((status ?? '').trim().toLowerCase()) {
    case 'suspended':
      return 'Suspended';
    case 'removed':
      return 'Removed';
    default:
      return 'Active';
  }
}
