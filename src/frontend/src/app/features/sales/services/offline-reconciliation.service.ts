import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { OFFLINE_RECONCILIATION_ENDPOINTS } from '../../../core/auth/auth.constants';
import type {
  ReconciliationCase,
  ReconciliationCasePage,
  ReconciliationStatusFilter,
  RecoverySubmissionResult,
  ResolveReconciliationCaseRequest,
} from './offline-reconciliation.models';

@Injectable({ providedIn: 'root' })
export class OfflineReconciliationService {
  private readonly http = inject(HttpClient);

  listCases(status: ReconciliationStatusFilter, page: number, pageSize: number): Observable<ReconciliationCasePage> {
    const params = new HttpParams().set('status', status).set('page', page).set('pageSize', pageSize);
    return this.http.get<ReconciliationCasePage>(OFFLINE_RECONCILIATION_ENDPOINTS.list, { params });
  }

  getCase(caseId: string): Observable<ReconciliationCase> {
    return this.http.get<ReconciliationCase>(OFFLINE_RECONCILIATION_ENDPOINTS.detail(caseId));
  }

  resolveCase(caseId: string, request: ResolveReconciliationCaseRequest): Observable<ReconciliationCase> {
    return this.http.post<ReconciliationCase>(OFFLINE_RECONCILIATION_ENDPOINTS.resolve(caseId), request);
  }

  /**
   * The server checksums the bundle's `invoices` using their exact raw text, so the bundle is spliced into the
   * body as a string rather than parsed and re-serialised.
   */
  submitRecoveryBundle(bundleText: string, operationId: string): Observable<RecoverySubmissionResult> {
    const body = '{"operationId":' + JSON.stringify(operationId) + ',"bundle":' + bundleText + '}';
    return this.http.post<RecoverySubmissionResult>(OFFLINE_RECONCILIATION_ENDPOINTS.recoverySubmissions, body, {
      headers: new HttpHeaders({ 'Content-Type': 'application/json' }),
    });
  }
}
