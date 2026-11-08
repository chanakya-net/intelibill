import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { EMPTY, of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { ExpiryPolicyFormComponent } from './expiry-policy-form.component';
import { ExpiryPolicyService } from '../services/expiry-policy.service';
import { expiryPolicyState } from '../../../../tests/expiry/expiry-policy.providers';
function setup(role: string) {
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
          readiness: () => EMPTY,
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
  return { fixture, component: fixture.componentInstance, update, scope };
}
describe('expiry policy operator form', () => {
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
    const { component, update } = setup('owner'); component.reason = 'Require dated stock'; component.policy = { ...component.policy, expiryRequired: true }; component.save();
    expect(update).toHaveBeenCalledWith({ expectedCatalogVersion: 1, reason: 'Require dated stock', policy: { mode: 'Ignore', expiryRequired: true, sellThroughExpiryDate: true, warningDays: 7, overrideAllowed: false } }, undefined);
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
