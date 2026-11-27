import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { DASHBOARD_ENDPOINTS } from '../../../core/auth/auth.constants';
import { DashboardDto, DashboardQueryParams } from './dashboard.models';

@Injectable({
  providedIn: 'root',
})
export class DashboardService {
  private readonly http = inject(HttpClient);

  getDashboard(params: DashboardQueryParams): Observable<DashboardDto> {
    let httpParams = new HttpParams();

    if (params.from) {
      httpParams = httpParams.set('from', params.from);
    }

    if (params.to) {
      httpParams = httpParams.set('to', params.to);
    }

    return this.http.get<DashboardDto>(DASHBOARD_ENDPOINTS.overview, { params: httpParams });
  }
}
