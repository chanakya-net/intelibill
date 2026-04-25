import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../core/auth/auth.service';
import { BankAccount } from '../services/bank-account.service';
import { BankAccountsFacade } from '../state/bank-accounts.facade';
import { BankAccountsPageComponent } from './bank-accounts-page.component';

describe('BankAccountsPageComponent', () => {
  const bankAccountsSignal = signal<BankAccount[]>([
    {
      id: 'b1',
      bankName: 'State Bank of India',
      accountNumber: '1234567890',
      accountType: 'Savings',
      ifscCode: 'SBIN0001234',
      accountHolderName: 'Chandra Kumar',
    },
  ]);
  const loadingSignal = signal(false);
  const errorSignal = signal('');
  const lastMutationSucceededSignal = signal(false);

  const bankAccountsFacade = {
    bankAccounts: bankAccountsSignal,
    isLoading: loadingSignal,
    errorMessage: errorSignal,
    lastMutationSucceeded: lastMutationSucceededSignal,
    load: vi.fn(),
    clearError: vi.fn(),
    clearMutationStatus: vi.fn(),
    deleteBankAccount: vi.fn(),
  };

  const sessionSignal = signal({
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    accessTokenExpiresAt: new Date(Date.now() + 60_000).toISOString(),
    refreshTokenExpiresAt: new Date(Date.now() + 120_000).toISOString(),
    rememberMe: true,
    user: {
      id: 'owner-1',
      email: 'owner@test.com',
      phoneNumber: null,
      firstName: 'Owner',
      lastName: 'One',
    },
    activeShopId: 'shop-1',
    shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Owner', isDefault: true, lastUsedAt: null }],
  });

  const authService = {
    session: sessionSignal,
  };

  beforeEach(() => {
    bankAccountsFacade.load.mockReset();
    bankAccountsFacade.clearError.mockReset();
    bankAccountsFacade.clearMutationStatus.mockReset();
    bankAccountsFacade.deleteBankAccount.mockReset();
    bankAccountsSignal.set([
      {
        id: 'b1',
        bankName: 'State Bank of India',
        accountNumber: '1234567890',
        accountType: 'Savings',
        ifscCode: 'SBIN0001234',
        accountHolderName: 'Chandra Kumar',
      },
    ]);
    loadingSignal.set(false);
    errorSignal.set('');
    lastMutationSucceededSignal.set(false);

    TestBed.configureTestingModule({
      imports: [BankAccountsPageComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
      providers: [
        { provide: BankAccountsFacade, useValue: bankAccountsFacade },
        { provide: AuthService, useValue: authService },
      ],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('loads bank accounts on init', () => {
    TestBed.createComponent(BankAccountsPageComponent);

    expect(bankAccountsFacade.load).toHaveBeenCalled();
  });

  it('opens manage overlay for adding', () => {
    const fixture = TestBed.createComponent(BankAccountsPageComponent);
    const component = fixture.componentInstance;

    component.onOpenAddAccount();
    expect(component.showManageOverlay()).toBe(true);
    expect(component.editingAccount()).toBeNull();
  });

  it('opens manage overlay for editing', () => {
    const fixture = TestBed.createComponent(BankAccountsPageComponent);
    const component = fixture.componentInstance;
    const account = bankAccountsSignal()[0];

    component.onOpenEditAccount(account);
    expect(component.showManageOverlay()).toBe(true);
    expect(component.editingAccount()).toEqual(account);
  });

  it('closes manage overlay on successful mutation', () => {
    const fixture = TestBed.createComponent(BankAccountsPageComponent);
    const component = fixture.componentInstance;

    component.onOpenAddAccount();
    expect(component.showManageOverlay()).toBe(true);

    lastMutationSucceededSignal.set(true);
    fixture.detectChanges();

    expect(component.showManageOverlay()).toBe(false);
    expect(bankAccountsFacade.clearMutationStatus).toHaveBeenCalled();
  });

  it('allows management only for owner role', () => {
    const fixture = TestBed.createComponent(BankAccountsPageComponent);
    const component = fixture.componentInstance;

    expect(component.canManageBankAccounts()).toBe(true);

    sessionSignal.set({
      ...sessionSignal(),
      shops: [{ shopId: 'shop-1', shopName: 'Main', role: 'Manager', isDefault: true, lastUsedAt: null }],
    });

    expect(component.canManageBankAccounts()).toBe(false);
  });

  it('displays error message when server fails to load', () => {
    const fixture = TestBed.createComponent(BankAccountsPageComponent);
    errorSignal.set('Failed to load bank accounts');
    fixture.detectChanges();

    const errorEl = fixture.nativeElement.querySelector('.error');
    expect(errorEl).toBeTruthy();
  });

  it('shows empty state when no accounts exist', () => {
    const fixture = TestBed.createComponent(BankAccountsPageComponent);
    bankAccountsSignal.set([]);
    fixture.detectChanges();

    const emptyState = fixture.nativeElement.querySelector('.empty-state');
    expect(emptyState).toBeTruthy();
  });
});
