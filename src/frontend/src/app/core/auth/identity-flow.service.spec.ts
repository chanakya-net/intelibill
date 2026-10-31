import { TestBed } from '@angular/core/testing';
import { of, firstValueFrom } from 'rxjs';
import { AuthService } from './auth.service';
import { IdentityFlowService } from './identity-flow.service';

describe('IdentityFlowService', () => {
  let actor = 'actor-1';
  const auth = {
    session: () => ({ user: { id: actor } }),
    isAuthenticated: () => !!actor,
    requestEmailChange: vi.fn(),
  };
  let service: IdentityFlowService;
  beforeEach(() => {
    actor = 'actor-1';
    sessionStorage.clear();
    auth.requestEmailChange.mockReset().mockReturnValue(of({ message: 'pending' }));
    TestBed.configureTestingModule({
      providers: [{ provide: AuthService, useValue: auth }],
    });
    service = TestBed.inject(IdentityFlowService);
  });
  afterEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });
  it('carries a fresh proof into the pending email change', async () => {
    const outcome = await firstValueFrom(
      service.perform(
        { kind: 'email', email: 'new@example.com' },
        { proof: 'recent', expiresAt: new Date(Date.now() + 300000).toISOString() },
      ),
    );
    expect(outcome).toBe('email-pending');
    expect(auth.requestEmailChange).toHaveBeenCalledWith('new@example.com', 'recent');
  });
  it.each(['signed-out', 'expired'] as const)(
    'rejects a %s proof without requesting the change',
    async (reason) => {
      if (reason === 'signed-out') actor = '';
      const expiresAt = new Date(
        reason === 'expired' ? Date.now() - 1000 : Date.now() + 300000,
      ).toISOString();
      await expect(
        firstValueFrom(
          service.perform({ kind: 'email', email: 'new@example.com' }, { proof: 'recent', expiresAt }),
        ),
      ).rejects.toBeTruthy();
      expect(auth.requestEmailChange).not.toHaveBeenCalled();
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
