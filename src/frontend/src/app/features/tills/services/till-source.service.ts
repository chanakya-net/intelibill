import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, throwError, defer, firstValueFrom } from 'rxjs';
import { scopedRequestContext } from '../../../core/context/scoped-request';
import { TillDraftService } from './till-draft.service';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { ScopeOwner } from '../../../core/context/shop-scope';
import { TillService } from './till.service';
export interface TillSourceFields {
  tillOwner?: ScopeOwner;
  tillParentSaleId?: string;
  till?: import('./till.models').TillPostingContext;
  tenderLine?: import('./till.models').TenderLine;
  tenderLines?: import('./till.models').TenderLine[];
  settlementEvidence?: import('./till.models').TillSettlementEvidence;
  operationKey?: string;
  refundTender?: import('./till.models').TenderLine;
  refundConfirmed?: boolean;
}
@Injectable({ providedIn: 'root' })
export class TillSourceService {
  private readonly http = inject(HttpClient);
  private readonly till = inject(TillService);
  private readonly drafts = inject(TillDraftService);
  post<T>(
    path: string,
    action: string,
    target: string,
    cycle: string,
    request: object,
    recover?: (response: unknown) => Promise<T>,
  ): Observable<T> {
    const { tillOwner, tillParentSaleId, ...body } = request as TillSourceFields;
    if (!tillOwner) {
      if (body.till || body.tenderLine || body.tenderLines || body.settlementEvidence)
        return throwError(() => new Error('Till request owner is missing.'));
      return this.http.post<T>(`${API_BASE_URL}${path}`, body);
    }
    const owner = { ...tillOwner },
      frozen = {
        ...structuredClone(body),
        ...(tillParentSaleId ? { __parentSaleId: tillParentSaleId } : {}),
      };
    return defer(async () => {
      const prior = (await this.drafts.list(owner)).some(
        (x) => x.pending && x.targetId.startsWith(`${action}:${target}:`),
      );
      if (
        prior ||
        frozen.till ||
        frozen.tenderLine ||
        frozen.tenderLines ||
        frozen.settlementEvidence ||
        'tillReconciliationEvidence' in frozen
      )
        return firstValueFrom(
          this.till.execute<T>(owner, action, target, cycle, frozen, path, true, recover),
        );
      return firstValueFrom(
        this.http.post<T>(`${API_BASE_URL}${path}`, frozen, {
          context: scopedRequestContext(owner),
        }),
      );
    });
  }
}
