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

@Component({
  selector: 'app-sale-customer-section',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, AutoCompleteModule, InputTextModule, TranslocoPipe],
  templateUrl: './sale-customer-section.component.html',
})
export class SaleCustomerSectionComponent implements OnInit {
  @Input() selectedCustomer: CustomerDto | null = null;
  @Input({ required: true }) customerNameControl!: FormControl<string>;
  @Input({ required: true }) customerPhoneControl!: FormControl<string>;
  @Input() customerNameSuggestions: string[] = [];
  @Input() isOfflineMode = false;

  @Output() customerSelected = new EventEmitter<CustomerDto | null>();
  @Output() searchCustomers = new EventEmitter<string>();
  @Output() customerSuggestionSelected = new EventEmitter<string>();

  ngOnInit(): void {
    this.customerNameControl.valueChanges.subscribe((value) => {
      if (!value.trim()) {
        this.customerSelected.emit(null);
      }
    });
  }

  onCustomerNameSearch(event: AutoCompleteCompleteEvent): void {
    this.searchCustomers.emit(event.query);
  }

  onCustomerSelect(value: string): void {
    this.customerSuggestionSelected.emit(value);
    this.customerSelected.emit(this.selectedCustomer);
  }
}
