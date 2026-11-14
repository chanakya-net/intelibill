import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

interface BootstrapContext {
  available: boolean;
  canResume: boolean;
  requestToken?: string;
}

interface PendingEnrollment {
  state: 'enrollmentRequired';
  expiresAt: string;
}

@Injectable({ providedIn: 'root' })
export class BootstrapApi {
  private readonly http = inject(HttpClient);
  private readonly path = '/api/admin/auth/bootstrap';

  context() {
    return this.http.get<BootstrapContext>(this.path + '/context');
  }

  enrollment() {
    return this.http.get<PendingEnrollment>(this.path + '/enrollment');
  }

  submit(requestToken: string, password: string, proofToken: string) {
    return this.http.post<PendingEnrollment>(
      this.path, { proofToken, password }, this.csrf(requestToken),
    );
  }

  resume(requestToken: string, password: string, email: string) {
    return this.http.post<PendingEnrollment>(
      this.path + '/enrollment/resume', { email, password }, this.csrf(requestToken),
    );
  }

  private csrf(token: string) {
    return { headers: { 'X-Bootstrap-CSRF': token } };
  }
}
