import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { TagModule } from 'primeng/tag';

import { Supplier } from '../../services/supplier.service';

@Component({
  selector: 'app-supplier-info-card',
  standalone: true,
  imports: [CommonModule, TagModule, TranslocoPipe],
  templateUrl: './supplier-info-card.component.html',
  styleUrl: './supplier-info-card.component.scss',
})
export class SupplierInfoCardComponent {
  @Input({ required: true }) supplier!: Supplier;
}

