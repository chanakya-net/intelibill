import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { problemCode } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import { finalize } from 'rxjs';
import { CatalogueApi, factLabels, type TrialDraft } from './catalogue-api.service';
import { invalid, parseCount, parseText } from './catalogue-money';

@Component({
  selector: 'adm-trial-policy-editor',
  imports: [ButtonDirective],
  templateUrl: './trial-policy-editor.component.html',
  styleUrls: ['../form-page.css', './catalogue-page.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TrialPolicyEditorComponent {
  private readonly api = inject(CatalogueApi);
  private readonly destroy = inject(DestroyRef);
  readonly draft = input<TrialDraft | null>(null);
  readonly saved = output<TrialDraft>();
  readonly conflict = output<void>();
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly cleared = signal(false);

  protected label(fact: string) {
    return factLabels[fact] ?? fact;
  }

  protected submit(event: SubmitEvent, form: HTMLFormElement) {
    event.preventDefault();
    if (this.busy() || this.cleared()) return;
    const data = new FormData(form);
    const caps = ['activeMemberCap', 'maxConcurrentTrialShops'].map((name) => parseCount(String(data.get(name) ?? '')));
    if (caps.includes(invalid)) {
      this.error.set('Trial caps are whole numbers from 1 to 100,000. There is no unlimited trial.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    this.api.saveTrial({
      expectedVersion: this.draft()?.version ?? 0,
      activeMemberCap: caps[0] as number | null,
      maxConcurrentTrialShops: caps[1] as number | null,
      trialTermsVersion: parseText(String(data.get('trialTermsVersion') ?? '')),
    }).pipe(takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false))).subscribe({
      next: ({ draft }) => {
        this.notice.set('Trial policy draft saved. It is not published.');
        this.saved.emit(draft);
      },
      error: (error: HttpErrorResponse) => this.refuse(error),
    });
  }

  private refuse(error: HttpErrorResponse) {
    if (error.status === 409) {
      this.cleared.set(true);
      this.error.set('The trial policy draft changed elsewhere. Reload the catalogue before editing.');
      this.conflict.emit();
      return;
    }
    const code = problemCode(error.error) ?? '';
    this.error.set(code === 'Catalogue.PolicyVersionInvalid'
      ? 'Policy versions use letters, digits, dot, dash or underscore.'
      : 'The trial policy draft was not saved. Check every field and try again.');
  }
}
