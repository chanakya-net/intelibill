import { TestBed } from '@angular/core/testing';
import { of, firstValueFrom } from 'rxjs';
import { AuthService } from './auth.service';
import { ExternalAuthProvider } from './auth.models';
import { IdentityFlowService, IDENTITY_REDIRECT } from './identity-flow.service';

describe('IdentityFlowService', () => {
  const redirect = vi.fn();
  let actor = 'actor-1';
  const auth = {
    session: () => ({ user: { id: actor } }),
    isAuthenticated: () => !!actor,
    initializeExternalLogin: vi.fn(),
    reauthenticateExternal: vi.fn(),
    completeExternalLink: vi.fn(),
    unlinkExternalProvider: vi.fn(),
    requestEmailChange: vi.fn(),
  };
  let service: IdentityFlowService;
  beforeEach(() => {
    actor = 'actor-1';
    sessionStorage.clear();
    redirect.mockClear();
    auth.initializeExternalLogin
      .mockReset()
      .mockReturnValue(of('https://accounts.example/authorize?state=state-1'));
    auth.reauthenticateExternal
      .mockReset()
      .mockReturnValue(
        of({ proof: 'recent', expiresAt: new Date(Date.now() + 300000).toISOString() }),
      );
    auth.completeExternalLink.mockReset().mockReturnValue(of(void 0));
    auth.unlinkExternalProvider.mockReset().mockReturnValue(of(void 0));
    auth.requestEmailChange.mockReset().mockReturnValue(of({ message: 'pending' }));
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: auth },
        { provide: IDENTITY_REDIRECT, useValue: redirect },
      ],
    });
    service = TestBed.inject(IdentityFlowService);
  });
  afterEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });
  it('provider-only reauth completes original-account proof before requesting pending email', async () => {
    await firstValueFrom(
      service.startProviderReauth(ExternalAuthProvider.Google, {
        kind: 'email',
        email: 'new@example.com',
      }),
    );
    expect(redirect).toHaveBeenCalled();
    expect(await firstValueFrom(service.completeOAuth('code', 'state-1'))).toBe('email-pending');
    expect(auth.reauthenticateExternal).toHaveBeenCalledWith('code', 'state-1');
    expect(auth.requestEmailChange).toHaveBeenCalledWith('new@example.com', 'recent');
    expect(service.hasOAuthIntent()).toBe(false);
  });
  it('explicit linking uses dedicated callback and consumes context', async () => {
    await firstValueFrom(
      service.perform(
        { kind: 'link', provider: ExternalAuthProvider.Facebook },
        { proof: 'recent', expiresAt: new Date(Date.now() + 300000).toISOString() },
      ),
    );
    expect(await firstValueFrom(service.completeOAuth('code', 'state-1'))).toBe('security-updated');
    expect(auth.completeExternalLink).toHaveBeenCalledWith('code', 'state-1', 'recent');
    expect(service.hasOAuthIntent()).toBe(false);
  });
  it.each(['actor', 'state', 'expired'] as const)(
    'rejects %s mismatch without invoking a mutation',
    async (reason) => {
      await firstValueFrom(
        service.startProviderReauth(ExternalAuthProvider.Google, {
          kind: 'unlink',
          provider: ExternalAuthProvider.Google,
        }),
      );
      if (reason === 'actor') actor = 'actor-2';
      if (reason === 'expired') {
        vi.useFakeTimers();
        vi.setSystemTime(Date.now() + 360000);
      }
      await expect(
        firstValueFrom(service.completeOAuth('code', reason === 'state' ? 'other' : 'state-1')),
      ).rejects.toBeTruthy();
      expect(auth.reauthenticateExternal).not.toHaveBeenCalled();
      expect(auth.unlinkExternalProvider).not.toHaveBeenCalled();
      expect(service.hasOAuthIntent()).toBe(false);
    },
  );
  it('keeps email change proof out of redirect URLs and expires resume state', () => {
    service.saveVerification({
      challengeId: 'challenge',
      token: 'bearer',
      purpose: 'email-change',
    });
    expect(service.pendingVerification()?.token).toBe('bearer');
    vi.useFakeTimers();
    vi.setSystemTime(Date.now() + 31 * 60000);
    expect(service.pendingVerification()).toBeNull();
  });
  it('reopening pending email confirmation does not extend its original lifetime', () => {
    const proof = { challengeId: 'id', token: 'bearer', purpose: 'email-change' as const };
    service.saveVerification(proof);
    vi.useFakeTimers();
    vi.setSystemTime(Date.now() + 29 * 60000);
    service.saveVerification(proof);
    vi.setSystemTime(Date.now() + 2 * 60000);
    expect(service.pendingVerification()).toBeNull();
  });
});
