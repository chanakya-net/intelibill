import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { BANK_ACCOUNT_ENDPOINTS } from '../../../core/auth/auth.constants';

export interface BankAccount {
  readonly id: string;
  readonly bankName: string;
  readonly accountNumber: string;
  readonly accountType: string | null;
  readonly ifscCode: string | null;
  readonly accountHolderName: string | null;
}

export interface AddBankAccountRequest {
  readonly bankName: string;
  readonly accountNumber: string;
  readonly accountType: string | null;
  readonly ifscCode: string | null;
  readonly accountHolderName: string | null;
}

export interface UpdateBankAccountRequest {
  readonly bankName: string;
  readonly accountNumber: string;
  readonly accountType: string | null;
  readonly ifscCode: string | null;
  readonly accountHolderName: string | null;
}

@Injectable({ providedIn: 'root' })
export class BankAccountService {
  private readonly http = inject(HttpClient);

  getBankAccounts(): Observable<readonly BankAccount[]> {
    return this.http.get<readonly BankAccount[]>(BANK_ACCOUNT_ENDPOINTS.list);
  }

  addBankAccount(payload: AddBankAccountRequest): Observable<BankAccount> {
    return this.http.post<BankAccount>(BANK_ACCOUNT_ENDPOINTS.add, payload);
  }

  updateBankAccount(id: string, payload: UpdateBankAccountRequest): Observable<BankAccount> {
    return this.http.put<BankAccount>(BANK_ACCOUNT_ENDPOINTS.update(id), payload);
  }

  deleteBankAccount(id: string): Observable<void> {
    return this.http.delete<void>(BANK_ACCOUNT_ENDPOINTS.delete(id));
  }
}
