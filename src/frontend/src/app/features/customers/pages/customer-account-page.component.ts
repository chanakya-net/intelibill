import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { InputNumberModule } from 'primeng/inputnumber';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { InputGroupModule } from 'primeng/inputgroup';
import { InputGroupAddonModule } from 'primeng/inputgroupaddon';

import { CustomerAccount, CustomerService } from '../services/customer.service';
import { CURRENCY_ADDON_PT, CURRENCY_INPUT_GROUP_PT, CURRENCY_INPUT_NUMBER_PT } from '../../../shared/primeng-pt.config';

@Component({
  selector: 'app-customer-account-page',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    ButtonModule,
    CardModule,
    InputNumberModule,
    InputTextModule,
    TableModule,
    TagModule,
    TranslocoPipe,
    InputGroupModule,
    InputGroupAddonModule,
  ],
  templateUrl: './customer-account-page.component.html',
  styleUrl: './customer-account-page.component.scss',
})
export class CustomerAccountPageComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly customerService = inject(CustomerService);
  private readonly fb = inject(FormBuilder);

  readonly currencyGroupPt = CURRENCY_INPUT_GROUP_PT;
  readonly currencyAddonPt = CURRENCY_ADDON_PT;
  readonly currencyInputPt = CURRENCY_INPUT_NUMBER_PT;

  readonly isLoading = signal(false);
  readonly errorMessage = signal('');
  readonly account = signal<CustomerAccount | null>(null);

  readonly paymentForm = this.fb.nonNullable.group({
    amount: [0, [Validators.required, Validators.min(0.01)]],
    paymentDate: [new Date().toISOString().slice(0, 10), Validators.required],
    notes: ['', Validators.maxLength(255)],
  });

  readonly hasAccount = computed(() => this.account() !== null);

  constructor() {
    this.loadAccount();
  }

  paymentTagSeverity(entryType: number): 'success' | 'danger' | 'info' {
    return entryType === 2 ? 'success' : 'danger';
  }

  paymentTagLabel(entryType: number): string {
    return entryType === 2 ? 'customers.account.paymentReceived' : 'customers.account.saleDue';
  }

  onRecordPayment(): void {
    if (this.paymentForm.invalid) {
      this.paymentForm.markAllAsTouched();
      return;
    }

    const customerId = this.route.snapshot.paramMap.get('customerId');
    if (!customerId) {
      this.errorMessage.set('customers.account.notFound');
      return;
    }

    this.errorMessage.set('');
    this.isLoading.set(true);

    this.customerService
      .recordCustomerPayment(customerId, {
        amount: Number(this.paymentForm.controls.amount.value),
        paymentDate: this.paymentForm.controls.paymentDate.value,
        notes: this.paymentForm.controls.notes.value.trim() || null,
      })
      .subscribe({
        next: () => {
          this.paymentForm.patchValue({ amount: 0, notes: '' });
          this.loadAccount();
        },
        error: (error) => {
          this.errorMessage.set(error.error?.detail || 'customers.account.paymentFailed');
          this.isLoading.set(false);
        },
      });
  }

  private loadAccount(): void {
    const customerId = this.route.snapshot.paramMap.get('customerId');
    if (!customerId) {
      this.errorMessage.set('customers.account.notFound');
      return;
    }

    this.errorMessage.set('');
    this.isLoading.set(true);

    this.customerService.getCustomerAccount(customerId).subscribe({
      next: (account) => {
        this.account.set(account);
        this.isLoading.set(false);
      },
      error: (error) => {
        this.errorMessage.set(error.error?.detail || 'customers.account.loadFailed');
        this.isLoading.set(false);
      },
    });
  }
}
