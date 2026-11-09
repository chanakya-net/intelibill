import { Pipe, PipeTransform, inject } from '@angular/core';

import { LocaleFormattingService } from '../../core/i18n/locale-formatting.service';

@Pipe({ name: 'localeMoney', standalone: true, pure: false })
export class LocaleMoneyPipe implements PipeTransform {
  private readonly formatter = inject(LocaleFormattingService);

  transform(value: number | null | undefined): string {
    return this.formatter.formatMoney(value);
  }
}
