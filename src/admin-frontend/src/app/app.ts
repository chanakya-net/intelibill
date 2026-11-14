import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { FormSectionStylesComponent } from '@intelibill/ui';

@Component({
  selector: 'adm-root',
  imports: [FormSectionStylesComponent, RouterOutlet],
  template: `
    <ib-form-section-styles />
    <header>InteliBill</header>
    <router-outlet />
  `,
  styles: `
    header {
      padding: 1rem clamp(1rem, 4vw, 2.5rem);
      border-bottom: 1px solid var(--app-sand-300);
      color: var(--app-brown-900);
      font-family: var(--app-font-display);
      font-weight: 800;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {}
