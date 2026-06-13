import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';

@Component({
  selector: 'app-credit-notes-page',
  standalone: true,
  imports: [CommonModule, TranslocoPipe],
  templateUrl: './credit-notes-page.component.html',
  styleUrl: './credit-notes-page.component.scss',
})
export class CreditNotesPageComponent {}
