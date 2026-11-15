import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ConfirmDialog } from 'primeng/confirmdialog';

/** The calling page owns its ConfirmationService, message and accept/reject callbacks. */
@Component({
  selector: 'ib-confirmation-dialog',
  imports: [ConfirmDialog],
  template: '<p-confirmDialog appendTo="body" />',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConfirmationDialogComponent {}
