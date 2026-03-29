import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

import { ButtonModule } from 'primeng/button';

@Component({
  selector: 'app-not-found-page',
  standalone: true,
  imports: [CommonModule, RouterLink, ButtonModule],
  template: `
    <section class="mx-auto grid min-h-[60vh] max-w-xl place-content-center gap-4 text-center">
      <p class="text-xs font-semibold uppercase tracking-[0.2em] text-slate-500">404</p>
      <h2 class="text-3xl font-bold text-slate-900">Page Not Found</h2>
      <p class="text-slate-600">The route does not exist in the Intelibill workspace.</p>
      <div class="pt-2">
        <a routerLink="/">
          <p-button label="Go Back" icon="pi pi-arrow-left"></p-button>
        </a>
      </div>
    </section>
  `,
})
export class NotFoundPageComponent {}
