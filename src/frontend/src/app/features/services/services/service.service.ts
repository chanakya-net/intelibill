import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';

import { Observable } from 'rxjs';

import { SERVICE_ENDPOINTS } from '../../../core/auth/auth.constants';
import type {
  AddServiceRequest,
  Service,
  ServiceQuery,
  UpdateServiceRequest,
} from './service.models';

@Injectable({ providedIn: 'root' })
export class ServiceService {
  private readonly http = inject(HttpClient);

  getServices(query: ServiceQuery): Observable<readonly Service[]> {
    const params = new HttpParams()
      .set('search', query.search)
      .set('includeInactive', query.includeInactive);

    return this.http.get<readonly Service[]>(SERVICE_ENDPOINTS.list, { params });
  }

  addService(payload: AddServiceRequest): Observable<Service> {
    return this.http.post<Service>(SERVICE_ENDPOINTS.add, payload);
  }

  updateService(serviceId: string, payload: UpdateServiceRequest): Observable<void> {
    return this.http.patch<void>(SERVICE_ENDPOINTS.update(serviceId), payload);
  }

  activateService(serviceId: string): Observable<void> {
    return this.http.post<void>(SERVICE_ENDPOINTS.activate(serviceId), {});
  }

  deactivateService(serviceId: string): Observable<void> {
    return this.http.post<void>(SERVICE_ENDPOINTS.deactivate(serviceId), {});
  }
}
