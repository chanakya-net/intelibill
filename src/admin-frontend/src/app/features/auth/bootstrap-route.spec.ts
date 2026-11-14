import { provideLocationMocks } from '@angular/common/testing';
import { TestBed } from '@angular/core/testing';
import { SESSION_PORT } from '@intelibill/api';
import { Router } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { appConfig } from '../../app.config';

describe('first owner setup entry', () => {
  it('resolves the setup and enrollment pages without changing the provider session', async () => {
    TestBed.configureTestingModule({ providers: [...appConfig.providers, provideLocationMocks()] });
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/auth/bootstrap');
    expect(router.url).toBe('/auth/bootstrap');
    await router.navigateByUrl('/auth/bootstrap/enrollment');
    expect(router.url).toBe('/auth/bootstrap/enrollment');
    expect(TestBed.inject(SESSION_PORT).state()).toBe('unauthenticated');
  });
});
