import { Pipe, PipeTransform, inject } from '@angular/core';

import { LocaleFormattingService } from '../../core/i18n/locale-formatting.service';

@Pipe({ name: 'localeInstant', standalone: true, pure: false })
export class LocaleInstantPipe implements PipeTransform {
  private readonly formatter = inject(LocaleFormattingService);

  transform(value: string | null | undefined, timeZone?: string): string {
    return this.formatter.formatInstant(value, timeZone);
  }
}
