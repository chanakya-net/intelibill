import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';

import { ShopMembersTableComponent } from './shop-members-table.component';

describe('ShopMembersTableComponent', () => {
  const members = [
    { userId: 'user-1', fullName: 'Anita', email: 'anita@example.com', role: 'Owner' },
    { userId: 'user-2', firstName: 'Rahul', lastName: 'Sharma', phoneNumber: '9876543210', role: 'Staff' },
  ] as const;

  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [
        ShopMembersTableComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
    }),
  );

  it('renders members and emits role changes', () => {
    const fixture = TestBed.createComponent(ShopMembersTableComponent);
    const changes: unknown[] = [];
    fixture.componentInstance.members = members;
    fixture.componentInstance.currentUserRole = 'Owner';
    fixture.componentInstance.roleChanged.subscribe((value) => changes.push(value));
    fixture.detectChanges();

    const select = fixture.nativeElement.querySelector('select') as HTMLSelectElement;
    select.value = 'Manager';
    select.dispatchEvent(new Event('change'));

    expect(fixture.nativeElement.textContent).toContain('Anita');
    expect(changes.at(-1)).toEqual({ userId: 'user-1', role: 'Manager' });
  });

  it('emits member removal', () => {
    const fixture = TestBed.createComponent(ShopMembersTableComponent);
    const removed: string[] = [];
    fixture.componentInstance.members = members;
    fixture.componentInstance.currentUserRole = 'Owner';
    fixture.componentInstance.memberRemoved.subscribe((id) => removed.push(id));
    fixture.detectChanges();

    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    buttons[0].click();

    expect(removed).toEqual(['user-1']);
  });

  it('locks actions for non-owners', () => {
    const fixture = TestBed.createComponent(ShopMembersTableComponent);
    fixture.componentInstance.members = members;
    fixture.componentInstance.currentUserRole = 'Staff';
    fixture.detectChanges();

    const select = fixture.nativeElement.querySelector('select') as HTMLSelectElement;
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(select.disabled).toBe(true);
    expect(button.disabled).toBe(true);
  });
});
