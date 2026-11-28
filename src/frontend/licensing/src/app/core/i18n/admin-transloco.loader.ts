import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { type Translation, type TranslocoLoader } from '@ngneat/transloco';
import { type Observable } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class AdminTranslocoLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);

  getTranslation(lang: string): Observable<Translation> {
    return this.http.get<Translation>(`/assets/i18n/${lang}.json`);
  }
}
