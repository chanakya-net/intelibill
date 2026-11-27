import { Component, effect, inject, untracked } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { OperatingAccessHintService } from './operating-access-hint.service';

@Component({
  selector: 'app-definition-access-notice',
  imports: [ButtonModule, TranslocoPipe],
  template: `
    @if (access.blockingMessage(); as message) {
      <div class="ib-error-banner flex flex-wrap items-center justify-between gap-3">
        <p class="m-0" role="alert">{{ message | transloco }}</p>
        <p-button
          [label]="'billing.status.refresh' | transloco"
          severity="secondary"
          [outlined]="true"
          [loading]="access.checking()"
          (onClick)="access.refresh()"
        />
      </div>
    }
  `,
})
export class DefinitionAccessNoticeComponent {
  readonly access = inject(OperatingAccessHintService);

  constructor() {
    effect(() => {
      if (this.access.scope()?.shopId) untracked(() => void this.access.refresh());
    });
  }
}
