import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { DASHBOARD_ENDPOINTS } from '../../../core/auth/auth.constants';

export interface DashboardDto {
  readonly generatedAt: string; // ISO date string
  readonly salesCount: number;
}

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);

  getDashboard(): Observable<DashboardDto> {
    return this.http.get<DashboardDto>(DASHBOARD_ENDPOINTS.summary);
  }
}
