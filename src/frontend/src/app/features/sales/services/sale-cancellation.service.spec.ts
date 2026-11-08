import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { REQUEST_SCOPE } from '../../../core/context/scoped-request';
import { API_BASE_URL } from '../../../core/auth/auth.constants';
import { SaleCancellationService } from './sale-cancellation.service';
describe('SaleCancellationService', () => {
  it('binds the frozen cancellation and outcome lookup to their owner', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const service = TestBed.inject(SaleCancellationService);
    const http = TestBed.inject(HttpTestingController);
    const owner = { userId: 'u', shopId: 's' };
    const payload = {
      operationKey: 'key',
      expectedVersion: 7,
      reason: 'Duplicate sale',
      goodsRecoveredConfirmed: true,
      cashRefundConfirmed: false,
    };
    service.cancel('sale', payload, owner).subscribe();
    const post = http.expectOne(`${API_BASE_URL}/sales/sale/cancellations`);
    expect(post.request.body).toEqual(payload);
    expect(post.request.context.get(REQUEST_SCOPE)).toEqual(owner);
    post.flush({});
    service.getOperation('a/b', owner).subscribe();
    const lookup = http.expectOne(`${API_BASE_URL}/financial-operations/sale-cancellation/a%2Fb`);
    expect(lookup.request.context.get(REQUEST_SCOPE)).toEqual(owner);
    lookup.flush({ response: {} });
    http.verify();
  });
});
