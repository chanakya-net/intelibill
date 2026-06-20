import 'package:flutter_test/flutter_test.dart';
import 'package:dio/dio.dart';
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
}
