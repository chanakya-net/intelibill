import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SupplierService } from './supplier.service';

describe('SupplierService bounded reads', () => {
  let service: SupplierService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(SupplierService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('serializes management filters and a literal remote search before paging', () => {
    service
      .getDirectory({
        search: ' Late %_ ',
        status: 'inactive',
        pageNumber: 3,
        pageSize: 100,
        includeSystem: false,
      })
      .subscribe();
    const request = http.expectOne((req) => req.url.endsWith('/suppliers/directory'));
    expect(
      Object.fromEntries(
        request.request.params.keys().map((key) => [key, request.request.params.get(key)]),
      ),
    ).toEqual({
      search: 'Late %_',
      status: 'inactive',
      pageNumber: '3',
      pageSize: '100',
      include_system: 'false',
    });
    request.flush({ items: [], totalCount: 0, pageNumber: 3, pageSize: 100, summary: {} });
  });
  it('defaults to a bounded page including system suppliers for selectors', () => {
    service.getDirectory({}).subscribe();
    const request = http.expectOne((req) => req.url.endsWith('/suppliers/directory'));
    expect(request.request.params.get('pageSize')).toBe('20');
    expect(request.request.params.get('include_system')).toBe('true');
    request.flush({ items: [], totalCount: 0, pageNumber: 1, pageSize: 20, summary: {} });
  });
  it('restores the exact ID independently of search results', () => {
    service.getSupplier('late-id').subscribe((value) => expect(value.supplierId).toBe('late-id'));
    http
      .expectOne((req) => req.url.endsWith('/suppliers/late-id'))
      .flush({ supplierId: 'late-id' });
  });
  it('retains the legacy array endpoint for compatibility', () => {
    service.getSuppliers().subscribe((value) => expect(value).toEqual([]));
    http.expectOne((req) => req.url.endsWith('/suppliers')).flush([]);
  });
});
