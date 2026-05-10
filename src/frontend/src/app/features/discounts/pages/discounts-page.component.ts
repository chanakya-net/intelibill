import { Component } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';

@Component({
  selector: 'app-discounts-page',
  standalone: true,
  imports: [TranslocoPipe],
  template: `
    <div class="p-4">
      <h1 class="text-2xl font-semibold">{{ 'discounts.title' | transloco }}</h1>
    </div>
  `,
})
export class DiscountsPageComponent {}
