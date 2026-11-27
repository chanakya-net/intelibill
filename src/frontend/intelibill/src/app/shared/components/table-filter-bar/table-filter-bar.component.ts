import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { Table, TableModule } from 'primeng/table';

@Component({
  selector: 'app-table-filter-bar',
  standalone: true,
  imports: [
    FormsModule,
    ButtonModule,
    IconFieldModule,
    InputIconModule,
    InputTextModule,
    TableModule,
    TranslocoPipe,
  ],
  templateUrl: './table-filter-bar.component.html',
  styleUrl: './table-filter-bar.component.scss',
})
export class TableFilterBarComponent {
  @Input({ required: true }) table!: Table;
  @Input() placeholder = '';
  @Input() searchValue = '';
  @Output() searchValueChange = new EventEmitter<string>();

  onSearchChange(value: string): void {
    this.searchValueChange.emit(value);
    this.table.filterGlobal(value, 'contains');
  }

  onClear(): void {
    this.searchValueChange.emit('');
    this.table.clear();
  }
}
