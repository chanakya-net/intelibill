import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/discounts/data/data_sources/discounts_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/discounts/data/dto/discount_rule_dto.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_query.dart';
import 'package:mocktail/mocktail.dart';

import '../../../mocks/mock_api_client.dart';

void main() {
  late MockApiClient mockApiClient;
  late DiscountsRemoteDataSourceImpl remoteDataSource;

  setUp(() {
    mockApiClient = MockApiClient();
    remoteDataSource = DiscountsRemoteDataSourceImpl(mockApiClient);
  });

  group('DiscountsRemoteDataSourceImpl', () {
    test('calls /discounts with full query params', () async {
      when(
        () => mockApiClient.get<Map<String, dynamic>>(
          any<String>(),
          queryParameters: any<Map<String, dynamic>>(
            named: 'queryParameters',
          ),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: {
            'items': <dynamic>[],
            'totalCount': 0,
            'pageNumber': 2,
            'pageSize': 25,
          },
          statusCode: 200,
          requestOptions: RequestOptions(path: '/discounts'),
        ),
      );

      final response = await remoteDataSource.getDiscountRules(
        const DiscountRulesQuery(
          statusFilter: 'active',
          ruleTypeFilter: 'BatchPercentage',
          search: 'summer',
          page: 2,
          pageSize: 25,
        ),
      );

      expect(response.totalCount, 0);
      verify(
        () => mockApiClient.get<Map<String, dynamic>>(
          '/discounts',
          queryParameters: {
            'status': 'active',
            'ruleType': 'BatchPercentage',
            'search': 'summer',
            'sort': 'created_desc',
            'page': 2,
            'pageSize': 25,
          },
        ),
      ).called(1);
    });

    test('omits optional filters when defaults are used', () async {
      when(
        () => mockApiClient.get<Map<String, dynamic>>(
          any<String>(),
          queryParameters: any<Map<String, dynamic>>(
            named: 'queryParameters',
          ),
        ),
      ).thenAnswer(
        (_) async => Response(
          data: {
            'items': <dynamic>[],
            'totalCount': 0,
            'pageNumber': 1,
            'pageSize': 20,
          },
          statusCode: 200,
          requestOptions: RequestOptions(path: '/discounts'),
        ),
      );

      await remoteDataSource.getDiscountRules(const DiscountRulesQuery());

      verify(
        () => mockApiClient.get<Map<String, dynamic>>(
          '/discounts',
          queryParameters: {
            'sort': 'created_desc',
            'page': 1,
            'pageSize': 20,
          },
        ),
      ).called(1);
    });

    test('calls /discounts/{id} and parses detail dto', () async {
      when(
        () => mockApiClient.get<Map<String, dynamic>>(any<String>()),
      ).thenAnswer(
        (_) async => Response(
          data: {
            'id': 'rule-42',
            'ruleType': 'SalePercentage',
            'name': 'Holiday sale',
            'percentage': 12.5,
            'isActive': true,
            'belowCostConfirmed': false,
            'createdAt': '2026-05-01T00:00:00.000Z',
            'description': 'Limited time',
          },
          statusCode: 200,
          requestOptions: RequestOptions(path: '/discounts/rule-42'),
        ),
      );

      final response = await remoteDataSource.getDiscountRule('rule-42');

      expect(response, isA<DiscountRuleDto>());
      expect(response.id, 'rule-42');
      expect(response.name, 'Holiday sale');
      expect(response.description, 'Limited time');
      verify(
        () => mockApiClient.get<Map<String, dynamic>>('/discounts/rule-42'),
      ).called(1);
    });
  });
}
