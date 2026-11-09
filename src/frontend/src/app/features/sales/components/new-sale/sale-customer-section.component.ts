import { customerOptionLabel } from '../../../../shared/models/party-directory.models';
import { DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnInit, Output } from '@angular/core';
import { AutoCompleteCompleteEvent, AutoCompleteModule } from 'primeng/autocomplete';
import { InputTextModule } from 'primeng/inputtext';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';

export interface CustomerDto {
  readonly customerId: string;
  readonly name: string;
  readonly phoneNumber: string;
  readonly address?: string | null;
}

let customerSectionSequence = 0;

@Component({
  selector: 'app-sale-customer-section',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, AutoCompleteModule, InputTextModule, TranslocoPipe],
  templateUrl: './sale-customer-section.component.html',
})
export class SaleCustomerSectionComponent implements OnInit {
  readonly controlPrefix = `sale-customer-${++customerSectionSequence}`;
  @Input() selectedCustomer: CustomerDto | null = null;
  @Input({ required: true }) customerNameControl!: FormControl<string>;
  @Input({ required: true }) customerPhoneControl!: FormControl<string>;
  @Input() customerNameSuggestions: string[] = [];
  @Input() customers: readonly CustomerDto[] = [];
  @Input() isOfflineMode = false;
  @Input() searchLoading = false;
  @Input() searchError = false;
  private readonly destroyRef = inject(DestroyRef);

  @Output() customerSelected = new EventEmitter<CustomerDto | null>();
  @Output() searchCustomers = new EventEmitter<string>();

  ngOnInit(): void {
    this.customerNameControl.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((value) => {
        if (!value.trim()) {
          this.customerSelected.emit(null);
        }
      });
  }

  onCustomerNameSearch(event: AutoCompleteCompleteEvent): void {
    this.searchCustomers.emit(event.query);
  }

  onCustomerSelect(value: string): void {
    const normalized = value.trim().toLowerCase();
    if (!normalized) {
      this.customerSelected.emit(null);
      return;
    }

    const matches = this.customers.filter(
      (customer) =>
        customerOptionLabel(customer).toLowerCase() === normalized ||
        customer.name.trim().toLowerCase() === normalized,
    );
    this.customerSelected.emit(matches.length === 1 ? (matches[0] ?? null) : null);
  }
}
