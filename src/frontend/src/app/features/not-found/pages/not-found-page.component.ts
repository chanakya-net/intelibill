import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';

@Component({
  selector: 'app-not-found-page',
  standalone: true,
  imports: [RouterLink, ButtonModule, TranslocoPipe],
  template: `
    <section class="mx-auto grid min-h-[60vh] max-w-xl place-content-center gap-4 text-center">
      <p class="text-xs font-semibold uppercase tracking-[0.2em] text-orange-700">404</p>
      <h2 class="text-3xl font-bold text-slate-800">{{ 'notFound.title' | transloco }}</h2>
      <p class="text-stone-600">{{ 'notFound.description' | transloco }}</p>
      <div class="pt-2">
        <a routerLink="/">
          <p-button [label]="'notFound.goBack' | transloco" icon="pi pi-arrow-left"></p-button>
        </a>
      </div>
    </section>
  `,
})
export class NotFoundPageComponent {}
