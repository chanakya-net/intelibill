import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { AuthService } from '../auth/auth.service';
import { ShellComponent } from './shell.component';

describe('ShellComponent', () => {
  const authService = {
    needsShopSetup: signal(false),
    signOutAndRedirect: vi.fn<AuthService['signOutAndRedirect']>(),
  };

  function setup(): ShellComponent {
    TestBed.configureTestingModule({
      imports: [ShellComponent, RouterTestingModule.withRoutes([])],
      providers: [{ provide: AuthService, useValue: authService }],
    });

    const fixture = TestBed.createComponent(ShellComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    authService.signOutAndRedirect.mockReset();
    authService.signOutAndRedirect.mockReturnValue(of(void 0));
    authService.needsShopSetup.set(false);
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('signs out when create-shop overlay close is requested', () => {
    const component = setup();

    component.onCreateShopOverlayClose();

    expect(authService.signOutAndRedirect).toHaveBeenCalledTimes(1);
    expect(component.isSigningOut()).toBe(false);
  });
});
