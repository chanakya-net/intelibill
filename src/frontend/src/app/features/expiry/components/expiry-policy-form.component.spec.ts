import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { EMPTY, of, Subject, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { ExpiryPolicyFormComponent } from './expiry-policy-form.component';
import { ExpiryPolicyService, type ExpiryReadiness } from '../services/expiry-policy.service';
import { expiryPolicyState } from '../../../../tests/expiry/expiry-policy.providers';
function setup(role: string, readiness = vi.fn<ExpiryPolicyService['readiness']>(() => EMPTY)) {
  const scope = { id: 'scope-one' };
  const update = vi.fn(() => of({ ...expiryPolicyState, catalogVersion: 2 }));
  TestBed.configureTestingModule({
    imports: [
      ExpiryPolicyFormComponent,
      TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
    ],
    providers: [
      {
        provide: ExpiryPolicyService,
        useValue: {
          scopeIdentity: () => scope.id,
          read: () => of(expiryPolicyState),
          businessTimeZone: () => of('Asia/Kolkata'),
          readiness,
          role: () => role,
          activeShopId: () => 'shop',
          update,
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(ExpiryPolicyFormComponent);
  fixture.componentRef.setInput('shopId', 'shop');
  fixture.componentRef.setInput('canEdit', role === 'owner');
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, update, scope, readiness };
}
describe('expiry policy operator form', () => {
  it('clears prior counts after save and displays only readiness for the returned catalog', () => {
    const response = new Subject<ExpiryReadiness>();
    const readiness = vi
      .fn<ExpiryPolicyService['readiness']>()
      .mockReturnValueOnce(of(ready(1, 0)))
      .mockReturnValueOnce(response);
    const { component } = setup('owner', readiness);
    expect(component.readiness()?.results.missingDateLots).toBe(0);
    component.reason = 'Require expiry';
    component.save();
    expect(component.state()?.catalogVersion).toBe(2);
    expect(component.readiness()).toBeNull();
    expect(readiness).toHaveBeenCalledTimes(2);
    response.next(ready(2, 4));
    expect(component.readiness()?.results.missingDateLots).toBe(4);
  });
  it('shows a readiness failure and retries without changing the policy draft', () => {
    const readiness = vi
      .fn<ExpiryPolicyService['readiness']>()
      .mockReturnValueOnce(throwError(() => new Error('network')))
      .mockReturnValueOnce(of(ready(1, 3)));
    const { component, fixture } = setup('owner', readiness);
    component.reason = 'Unsubmitted reason';
    fixture.changeDetectorRef.markForCheck();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain(
      'expiry.readinessFailed',
    );
    const button = Array.from(fixture.nativeElement.querySelectorAll('button')).find((x) =>
      (x as HTMLElement).textContent?.includes('expiry.retryReadiness'),
    ) as HTMLButtonElement;
    expect(button).toBeTruthy();
    button.click();
    expect(component.readiness()?.results.missingDateLots).toBe(3);
    expect(component.reason).toBe('Unsubmitted reason');
  });
  it('rejects an old readiness response after a newer policy save and after leaving scope', () => {
    const oldResponse = new Subject<ExpiryReadiness>();
    const currentResponse = new Subject<ExpiryReadiness>();
    const readiness = vi
      .fn<ExpiryPolicyService['readiness']>()
      .mockReturnValueOnce(oldResponse)
      .mockReturnValueOnce(currentResponse);
    const { component, scope } = setup('owner', readiness);
    component.reason = 'Changed';
    component.save();
    oldResponse.next(ready(1, 99));
    expect(component.readiness()).toBeNull();
    currentResponse.next(ready(2, 4));
    expect(component.readiness()?.results.missingDateLots).toBe(4);
    scope.id = 'another-scope';
    currentResponse.next(ready(2, 88));
    expect(component.readiness()?.results.missingDateLots).toBe(4);
  });
  it('does not display a successful readiness response for a different catalog revision', () => {
    const { component, fixture } = setup(
      'owner',
      vi.fn(() => of(ready(99, 88))),
    );
    expect(component.readiness()).toBeNull();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain(
      'expiry.readinessFailed',
    );
  });
  it('clears diagnostic counts when a save fails with an unknown server outcome', () => {
    const { component, update } = setup(
      'owner',
      vi.fn(() => of(ready(1, 0))),
    );
    update.mockReturnValueOnce(throwError(() => new Error('lost response')));
    component.reason = 'Changed';
    component.save();
    expect(component.message()).toBe('expiry.saveFailed');
    expect(component.readiness()).toBeNull();
  });
  it('shows policy/version while Staff edits and direct save remain disabled', () => {
    const { fixture, component, update } = setup('staff');
    expect(component.state()?.catalogVersion).toBe(1);
    expect(fixture.nativeElement.querySelector('fieldset').disabled).toBe(true);
    component.reason = 'Attempt';
    component.save();
    expect(update).not.toHaveBeenCalled();
  });
  it('refuses an unsubmitted policy draft after its owning scope epoch changes', () => {
    const { component, update, scope } = setup('owner');
    component.reason = 'Old scope edit';
    scope.id = 'scope-two';
    component.save();
    expect(update).not.toHaveBeenCalled();
  });
  it('sends only policy settings, leaving the policy version as server-owned state', () => {
    const { component, update } = setup('owner');
    component.reason = 'Require dated stock';
    component.policy = { ...component.policy, expiryRequired: true };
    component.save();
    expect(update).toHaveBeenCalledWith(
      {
        expectedCatalogVersion: 1,
        reason: 'Require dated stock',
        policy: {
          mode: 'Ignore',
          expiryRequired: true,
          sellThroughExpiryDate: true,
          warningDays: 7,
          overrideAllowed: false,
        },
      },
      undefined,
    );
  });
  it('requires a reason and sends the item inheritance reset with expected version', () => {
    const { fixture, component, update } = setup('owner');
    fixture.componentRef.setInput('itemId', 'item');
    fixture.detectChanges();
    component.save();
    expect(update).not.toHaveBeenCalled();
    expect(component.message()).toBe('expiry.reasonRequired');
    component.reason = ' Return to shop rule ';
    component.inherit = true;
    component.save();
    expect(update).toHaveBeenCalledWith(
      { expectedCatalogVersion: 1, policy: null, reason: 'Return to shop rule' },
      'item',
    );
    expect(component.state()?.catalogVersion).toBe(2);
    expect(component.reason).toBe('');
  });
});

function ready(catalogVersion: number, missingDateLots: number): ExpiryReadiness {
  return {
    catalogVersion,
    businessDate: '2026-11-09',
    results: {
      totalLots: 10,
      missingDateLots,
      expiredLots: 0,
      quarantinedLots: 0,
      physicalQuantity: 10,
      quarantinedQuantity: 0,
      items: [],
    },
  };
}
