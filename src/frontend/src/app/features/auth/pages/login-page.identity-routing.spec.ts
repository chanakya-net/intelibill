import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NavigationEnd, Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Store } from '@ngrx/store';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { Subject, filter, firstValueFrom } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { IdentityFlowService, IdentityOutcome } from '../../../core/auth/identity-flow.service';
import { LoginPageComponent } from './login-page.component';

describe('Legacy login identity callback with real Router', () => {
  afterEach(() => TestBed.resetTestingModule());
  it('shows security-update success after query-only navigation reuses the login component', async () => {
    let authenticated = true;
    const completion = new Subject<IdentityOutcome>();
    const auth = { isAuthenticated: () => authenticated, getLastRememberedIdentifier: () => '' };
    const flow = {
      hasOAuthIntent: () => true,
      completeOAuth: () => completion,
      clearOAuth: vi.fn(),
      afterSignIn: () => '/',
    };
    TestBed.configureTestingModule({
      imports: [LoginPageComponent, TranslocoTestingModule.forRoot({ langs: {} })],
      providers: [
        provideRouter([{ path: 'login', component: LoginPageComponent }]),
        { provide: AuthService, useValue: auth },
        { provide: IdentityFlowService, useValue: flow },
        { provide: Store, useValue: { selectSignal: () => signal(false) } },
      ],
    });
    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl(
      '/login?code=oauth-code&state=oauth-state',
      LoginPageComponent,
    );
    const router = TestBed.inject(Router);
    const navigated = firstValueFrom(
      router.events.pipe(
        filter(
          (event) =>
            event instanceof NavigationEnd && event.urlAfterRedirects === '/login?identity=updated',
        ),
      ),
    );
    authenticated = false;
    completion.next('security-updated');
    completion.complete();
    await navigated;
    harness.detectChanges();
    expect(router.url).toBe('/login?identity=updated');
    expect(harness.routeDebugElement?.componentInstance).toBe(component);
    expect(component.successMessage()).toBe('identity.signInAgain');
    expect(harness.routeNativeElement?.textContent).toContain('identity.signInAgain');
  });
});
