import { Location } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { expect, it, vi } from 'vitest';
import { CatalogueApi, type TrialDraft } from './catalogue-api.service';
import { CataloguePublicationComponent } from './catalogue-publication.component';
import type { PreparedPublication } from './catalogue-publication.models';

const draft: TrialDraft = {
  versionId: 'fixture-trial', status: 'draft', versionNumber: 1, version: 4, durationDays: 14,
  activeMemberCap: 5, maxConcurrentTrialShops: 2, trialTermsVersion: 'trial-local-1',
  missing: [], complete: true, updatedAt: '2026-11-16T00:00:00Z',
};

function render(key: string | null = null) {
  const snapshot: PreparedPublication = {
    operationKey: key ?? '', planId: null, versionId: draft.versionId, expectedVersion: draft.version,
    snapshotHash: 'fixture-hash', reason: 'Local approval', publishedAt: null, snapshot: {
      versionId: draft.versionId, versionNumber: 1, durationDays: 14, activeMemberCap: 5,
      maxConcurrentTrialShops: 2, trialTermsVersion: 'trial-local-1', developmentFixture: true,
    },
  };
  const replaceState = vi.fn();
  const api = {
    preparePublication: vi.fn((_target, { operationKey }) => {
      expect(replaceState).toHaveBeenLastCalledWith('/catalogue?publication=' + operationKey);
      return of({ ...snapshot, operationKey });
    }),
    confirmPublication: vi.fn((_prepared: PreparedPublication) => throwError(() => new HttpErrorResponse({ status: 0 }))),
    recoverPublication: vi.fn((operationKey: string) =>
      of({ ...snapshot, operationKey, publishedAt: '2026-11-16T00:00:10Z' })),
  };
  TestBed.configureTestingModule({ providers: [
    provideRouter([]),
    { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(key ? { publication: key } : {}) } } },
    { provide: Location, useValue: { path: () => '/catalogue', replaceState } },
    { provide: CatalogueApi, useValue: api },
  ] });
  const view = TestBed.createComponent(CataloguePublicationComponent);
  view.componentRef.setInput('trial', draft);
  view.detectChanges();
  const page = view.nativeElement as HTMLElement;
  const button = (text: string) =>
    [...page.querySelectorAll('button')].find(candidate => candidate.textContent?.trim() === text)!;
  const click = (text: string) => {
    button(text).click();
    view.detectChanges();
  };
  const prepare = () => {
    click('Review trial policy draft');
    page.querySelector<HTMLTextAreaElement>('textarea')!.value = 'Local approval';
    page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    view.detectChanges();
  };
  const approve = () => {
    page.querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
    view.detectChanges();
  };
  return { api, page, button, click, prepare, approve };
}

it('confirms and recovers the exact prepared publication after a lost response', () => {
  const { api, page, button, click, prepare, approve } = render();
  prepare();
  expect(page.textContent).toContain('Development fixture');
  expect(button('Confirm publication').disabled).toBe(true);
  approve();
  click('Confirm publication');
  const [prepared] = api.confirmPublication.mock.calls[0];
  expect(prepared).toMatchObject({ snapshotHash: 'fixture-hash', expectedVersion: 4, versionId: draft.versionId });
  expect(page.textContent).toContain('outcome is unconfirmed');
  expect(button('Close preview')).toBeUndefined();
  click('Check publication outcome');
  expect(api.recoverPublication).toHaveBeenCalledWith(prepared.operationKey);
  expect(page.textContent).toContain('Version published');
});

it('recovers the URL operation on reload', () => {
  const { api, page } = render('existing-operation-key');
  expect(api.recoverPublication).toHaveBeenCalledWith('existing-operation-key');
  expect(page.textContent).toContain('Version published');
});

it('locks a stale preview against confirmation', () => {
  const { api, page, button, click, prepare, approve } = render();
  api.confirmPublication.mockReturnValueOnce(throwError(() =>
    new HttpErrorResponse({ status: 409, error: { code: 'Catalogue.PublicationConflict' } })));
  prepare();
  approve();
  click('Confirm publication');
  expect(button('Confirm publication').disabled).toBe(true);
  expect(page.textContent).toContain('prepared terms changed');
});

it('clears protected terms when recovery loses the Licensing Manager grant', () => {
  const { api, page, click, prepare } = render();
  prepare();
  api.recoverPublication.mockReturnValueOnce(throwError(() =>
    new HttpErrorResponse({ status: 403, error: { code: 'Catalogue.AccessDenied' } })));
  click('Check publication outcome');
  expect(page.textContent).toContain('Catalogue access could not be confirmed.');
  expect(page.querySelector('[data-testid="publication-snapshot"]')).toBeNull();
});
