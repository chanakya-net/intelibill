import { ChangeDetectionStrategy, Component, ViewEncapsulation } from '@angular/core';

@Component({
  selector: 'ib-form-section-styles',
  template: '',
  styleUrls: [
    '../styles/ui-kit-layer-order.css',
    '../styles/ib-page-heading.css',
    '../styles/ib-panel.css',
    '../styles/ib-form-section.css',
  ],
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { hidden: '', 'aria-hidden': 'true' },
})
export class FormSectionStylesComponent {}
