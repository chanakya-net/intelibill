import { Pipe, PipeTransform, inject } from '@angular/core';

import { LocaleFormattingService } from '../../core/i18n/locale-formatting.service';

@Pipe({ name: 'localeBusinessDate', standalone: true, pure: false })
export class LocaleBusinessDatePipe implements PipeTransform {
  private readonly formatter = inject(LocaleFormattingService);

  transform(value: string | null | undefined): string {
    return this.formatter.formatBusinessDate(value);
  }
}
