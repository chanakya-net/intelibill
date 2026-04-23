import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { EXPENSE_ENDPOINTS } from '../../../core/auth/auth.constants';
import { ExpenseService, ExpenseDto, ExpenseListItemDto, PaginatedExpenses } from './expense.service';

describe('ExpenseService', () => {
  function setup(): { service: ExpenseService; http: HttpTestingController } {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    return {
      service: TestBed.inject(ExpenseService),
      http: TestBed.inject(HttpTestingController),
    };
  }

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  const makeExpenseList = (): ExpenseListItemDto[] => [
    {
      id: 'exp-1',
      amount: 500,
      categoryName: 'Rent',
      paidTo: 'Landlord',
      expenseDate: '2026-04-20T00:00:00Z',
      isVoided: false,
    },
  ];

  const makeExpenseDto = (): ExpenseDto => ({
    id: 'exp-1',
    shopId: 'shop-1',
    categoryId: 'cat-1',
    categoryName: 'Rent',
    amount: 500,
    paidTo: 'Landlord',
    description: 'Monthly rent',
    expenseDate: '2026-04-20T00:00:00Z',
    actorUserId: 'user-1',
    isVoided: false,
    originalExpenseId: null,
    createdAt: '2026-04-20T10:00:00Z',
  });

  const makePaginatedResponse = (): PaginatedExpenses => ({
    items: makeExpenseList(),
    totalCount: 1,
    pageNumber: 1,
    pageSize: 20,
  });

  it('sends GET request to list endpoint without search', () => {
    const { service, http } = setup();
    const response = makePaginatedResponse();

    service.getExpenses().subscribe((result) => {
      expect(result.items).toHaveLength(1);
      expect(result.items[0].id).toBe('exp-1');
      expect(result.totalCount).toBe(1);
    });

    const req = http.expectOne((r) => r.url === EXPENSE_ENDPOINTS.list && r.params.get('page') === '1' && r.params.get('pageSize') === '20');
    expect(req.request.method).toBe('GET');
    req.flush(response);
    http.verify();
  });

  it('sends GET request to list endpoint with search, page and pageSize', () => {
    const { service, http } = setup();
    const response = makePaginatedResponse();

    service.getExpenses('rent', 2, 10).subscribe((result) => {
      expect(result.items).toHaveLength(1);
    });

    const req = http.expectOne((r) =>
      r.url === EXPENSE_ENDPOINTS.list &&
      r.params.get('search') === 'rent' &&
      r.params.get('page') === '2' &&
      r.params.get('pageSize') === '10'
    );
    expect(req.request.method).toBe('GET');
    req.flush(response);
    http.verify();
  });

  it('sends GET request to detail endpoint', () => {
    const { service, http } = setup();
    const expense = makeExpenseDto();

    service.getExpenseById('exp-1').subscribe((result) => {
      expect(result.id).toBe('exp-1');
      expect(result.categoryName).toBe('Rent');
    });

    const req = http.expectOne(EXPENSE_ENDPOINTS.detail('exp-1'));
    expect(req.request.method).toBe('GET');
    req.flush(expense);
    http.verify();
  });

  it('sends POST request to record endpoint', () => {
    const { service, http } = setup();
    const expense = makeExpenseDto();
    const payload = {
      categoryName: 'Rent',
      amount: 500,
      paidTo: 'Landlord',
      description: 'Monthly rent',
      expenseDate: '2026-04-20',
    };

    service.recordExpense(payload).subscribe((result) => {
      expect(result.id).toBe('exp-1');
    });

    const req = http.expectOne(EXPENSE_ENDPOINTS.record);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush(expense);
    http.verify();
  });

  it('sends POST request to correct endpoint', () => {
    const { service, http } = setup();
    const expense = makeExpenseDto();
    const payload = {
      categoryName: 'Rent',
      amount: 550,
      paidTo: 'Landlord',
      description: 'Updated rent',
      expenseDate: '2026-04-20',
    };

    service.correctExpense('exp-1', payload).subscribe((result) => {
      expect(result.id).toBe('exp-1');
    });

    const req = http.expectOne(EXPENSE_ENDPOINTS.correct('exp-1'));
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(payload);
    req.flush(expense);
    http.verify();
  });
});
