import { formatDate } from '@angular/common';
import { LOCALE_ID, Pipe, PipeTransform, inject } from '@angular/core';

import { parseDateOnlyAsLocalDate } from '../utils/date-time.util';

@Pipe({
  name: 'dateOnly',
  standalone: true,
})
export class DateOnlyPipe implements PipeTransform {
  private readonly locale = inject(LOCALE_ID);

  transform(value: string | null | undefined, format = 'mediumDate'): string | null {
    if (!value) return null;
    return formatDate(parseDateOnlyAsLocalDate(value), format, this.locale);
  }
}
