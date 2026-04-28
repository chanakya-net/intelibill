import { CommonModule } from '@angular/common';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { TableModule } from 'primeng/table';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { ConfirmationService } from 'primeng/api';
import { BankAccountsFacade } from '../state/bank-accounts.facade';
import { BankAccount } from '../services/bank-account.service';
import { ManageBankAccountOverlayComponent } from '../components/manage-bank-account-overlay.component';
import { AuthService } from '../../../core/auth/auth.service';
import { TableFilterBarComponent } from '../../../shared/components/table-filter-bar/table-filter-bar.component';

@Component({
  selector: 'app-bank-accounts-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    CardModule,
    TableModule,
    ProgressSpinnerModule,
    ConfirmDialogModule,
    ManageBankAccountOverlayComponent,
    TableFilterBarComponent,
    TranslocoPipe,
  ],
  providers: [ConfirmationService],
  templateUrl: './bank-accounts-page.component.html',
  styleUrl: './bank-accounts-page.component.scss',
})
export class BankAccountsPageComponent {
  private readonly bankAccountsFacade = inject(BankAccountsFacade);
  private readonly confirmationService = inject(ConfirmationService);
  private readonly authService = inject(AuthService);

  readonly bankAccounts = this.bankAccountsFacade.bankAccounts;
  readonly searchValue = signal('');
  readonly isLoading = this.bankAccountsFacade.isLoading;
  readonly serverError = this.bankAccountsFacade.errorMessage;
  readonly lastMutationSucceeded = this.bankAccountsFacade.lastMutationSucceeded;

  readonly showManageOverlay = signal(false);
  readonly editingAccount = signal<BankAccount | null>(null);

  readonly session = this.authService.session;
  readonly activeShopRole = computed(() => {
    const session = this.session();
    if (!session) return '';
    const activeShop = session.shops.find((s) => s.shopId === session.activeShopId) ?? session.shops.find((s) => s.isDefault);
    return activeShop?.role ?? '';
  });
  readonly canManageBankAccounts = computed(() => this.activeShopRole().toLowerCase() === 'owner');

  constructor() {
    this.bankAccountsFacade.load();

    effect(() => {
      if (this.lastMutationSucceeded() && this.showManageOverlay()) {
        this.onCloseManageOverlay();
      }
    });
  }

  onOpenAddAccount(): void {
    this.editingAccount.set(null);
    this.showManageOverlay.set(true);
  }

  onOpenEditAccount(account: BankAccount): void {
    this.editingAccount.set(account);
    this.showManageOverlay.set(true);
  }

  onCloseManageOverlay(): void {
    this.showManageOverlay.set(false);
    this.editingAccount.set(null);
    this.bankAccountsFacade.clearMutationStatus();
  }

  onDeleteAccount(account: BankAccount): void {
    this.confirmationService.confirm({
      message: 'bankAccounts.deleteConfirm',
      header: 'bankAccounts.deleteAccount',
      icon: 'pi pi-exclamation-triangle',
      acceptButtonStyleClass: 'p-button-danger',
      rejectButtonStyleClass: 'p-button-secondary p-button-text',
      accept: () => {
        this.bankAccountsFacade.deleteBankAccount(account.id);
      },
    });
  }
}
