import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/features/credit_notes/data/data_sources/credit_note_remote_data_source.dart';
import 'package:mocktail/mocktail.dart';

class MockApiClient extends Mock implements ApiClient {}

void main() {
  late MockApiClient apiClient;

  setUp(() {
    apiClient = MockApiClient();
  });

  test('sends list query params', () async {
    when(
      () => apiClient.get<Map<String, dynamic>>(
        any(),
        queryParameters: any(named: 'queryParameters'),
      ),
    ).thenAnswer(
      (_) async => Response<Map<String, dynamic>>(
        data: {
          'items': <Map<String, dynamic>>[],
          'totalCount': 0,
          'pageNumber': 1,
          'pageSize': 20,
        },
        requestOptions: RequestOptions(path: '/credit-notes'),
      ),
    );

    final dataSource = CreditNoteRemoteDataSourceImpl(apiClient);
    await dataSource.getCreditNotes(
      search: 'abc',
      status: 'active',
      page: 2,
      pageSize: 10,
    );

    verify(
      () => apiClient.get<Map<String, dynamic>>(
        '/credit-notes',
        queryParameters: {
          'search': 'abc',
          'status': 'active',
          'page': 2,
          'pageSize': 10,
        },
      ),
    ).called(1);
  });

  test('voids note with reason', () async {
    when(
      () => apiClient.post<void>(
        '/credit-notes/CN-001/void',
        data: {'reason': 'Damaged'},
      ),
    ).thenAnswer(
      (_) async => Response<void>(
        statusCode: 200,
        requestOptions: RequestOptions(path: '/credit-notes/CN-001/void'),
      ),
    );

    final dataSource = CreditNoteRemoteDataSourceImpl(apiClient);
    await dataSource.voidCreditNote(code: 'CN-001', reason: 'Damaged');

    verify(
      () => apiClient.post<void>(
        '/credit-notes/CN-001/void',
        data: {'reason': 'Damaged'},
      ),
    ).called(1);
  });

  test('fetches print dto by credit note code', () async {
    when(
      () => apiClient.get<Map<String, dynamic>>(
        '/credit-notes/CN-001/print',
      ),
    ).thenAnswer(
      (_) async => Response<Map<String, dynamic>>(
        data: {
          'creditNoteId': 'cn-1',
          'code': 'CN-001',
          'status': 'active',
          'isUsable': true,
          'originalAmount': 250,
          'availableBalance': 150,
          'issuedAt': '2026-06-10T10:00:00.000Z',
          'expiresAt': '2026-06-30T10:00:00.000Z',
          'saleId': 'sale-1',
          'invoiceNumber': 'INV-001',
          'saleReturnId': 'ret-1',
          'returnNumber': 'RET-001',
          'customerDisplayName': 'John',
          'reason': 'Damaged item',
          'voidReason': null,
        },
        requestOptions: RequestOptions(path: '/credit-notes/CN-001/print'),
      ),
    );

    final dataSource = CreditNoteRemoteDataSourceImpl(apiClient);
    await dataSource.getCreditNotePrintByCode('CN-001');

    verify(
      () => apiClient.get<Map<String, dynamic>>(
        '/credit-notes/CN-001/print',
      ),
    ).called(1);
  });
}
