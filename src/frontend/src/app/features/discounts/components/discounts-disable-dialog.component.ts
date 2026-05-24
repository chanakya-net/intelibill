import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { TextareaModule } from 'primeng/textarea';

@Component({
  selector: 'app-discounts-disable-dialog',
  standalone: true,
  imports: [FormsModule, ButtonModule, DialogModule, TextareaModule, TranslocoPipe],
  templateUrl: './discounts-disable-dialog.component.html',
  styleUrl: './discounts-disable-dialog.component.scss',
})
export class DiscountsDisableDialogComponent {
  @Input({ required: true }) visible = false;
  @Input() disableSubmitting = false;
  @Input() disableReason = '';

  @Output() closeRequested = new EventEmitter<void>();
  @Output() disableReasonChange = new EventEmitter<string>();
  @Output() confirm = new EventEmitter<void>();

  onReasonChange(value: string): void {
    this.disableReasonChange.emit(value);
  }

  onCloseRequested(): void {
    this.closeRequested.emit();
  }

  onConfirm(): void {
    this.confirm.emit();
  }
}
