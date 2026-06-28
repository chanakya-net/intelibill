import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/discounts/data/data_sources/discounts_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/discounts/data/dto/discount_rule_dto.dart';
import 'package:intelibill_mobile/src/features/discounts/data/dto/discount_rules_response_dto.dart';
import 'package:intelibill_mobile/src/features/discounts/data/repositories/discounts_repository_impl.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_query.dart';
import 'package:mocktail/mocktail.dart';

class MockDiscountsRemoteDataSource extends Mock
    implements DiscountsRemoteDataSource {}

void main() {
  late MockDiscountsRemoteDataSource remoteDataSource;
  late DiscountsRepositoryImpl repository;

  setUpAll(() {
    registerFallbackValue(const DiscountRulesQuery());
  });

  setUp(() {
    remoteDataSource = MockDiscountsRemoteDataSource();
    repository = DiscountsRepositoryImpl(remoteDataSource);
  });

  group('DiscountsRepositoryImpl', () {
    test('maps list response into domain list rules and pagination', () async {
      when(() => remoteDataSource.getDiscountRules(any())).thenAnswer(
        (_) async => const DiscountRulesResponseDto(
          totalCount: 5,
          pageNumber: 1,
          pageSize: 20,
        ),
      );

      final result = await repository.getDiscountRules(
        const DiscountRulesQuery(),
      );

      expect(result.totalCount, 5);
      expect(result.pageNumber, 1);
      expect(result.hasMore, isFalse);
      expect(result.rules, isEmpty);
    });

    test('hasMore is true when more pages exist', () async {
      when(() => remoteDataSource.getDiscountRules(any())).thenAnswer(
        (_) async => const DiscountRulesResponseDto(
          totalCount: 50,
          pageNumber: 1,
          pageSize: 20,
        ),
      );

      final result = await repository.getDiscountRules(
        const DiscountRulesQuery(),
      );

      expect(result.hasMore, isTrue);
    });

    test('hasMore is false on last page', () async {
      when(() => remoteDataSource.getDiscountRules(any())).thenAnswer(
        (_) async => const DiscountRulesResponseDto(
          totalCount: 50,
          pageNumber: 3,
          pageSize: 20,
        ),
      );

      final result = await repository.getDiscountRules(
        const DiscountRulesQuery(),
      );

      expect(result.hasMore, isFalse);
    });

    test('rethrows AppExceptions from remote source', () async {
      final exception = AppException(
        failure: const Failure.network(message: 'offline'),
      );
      when(() => remoteDataSource.getDiscountRules(any())).thenThrow(exception);

      expect(
        repository.getDiscountRules(const DiscountRulesQuery()),
        throwsA(same(exception)),
      );
    });

    test('wraps serialization failure as serialization failure', () async {
      when(
        () => remoteDataSource.getDiscountRules(any()),
      ).thenThrow(const FormatException('bad json'));

      await expectLater(
        repository.getDiscountRules(const DiscountRulesQuery()),
        throwsA(
          isA<AppException>().having(
            (e) => e.failure,
            'failure',
            isA<SerializationFailure>().having(
              (f) => f.message,
              'message',
              'bad json',
            ),
          ),
        ),
      );
    });

    test('maps detail dto into domain entity', () async {
      when(() => remoteDataSource.getDiscountRule(any())).thenAnswer(
        (_) async => DiscountRuleDto(
          id: 'rule-1',
          ruleType: 'BatchPercentage',
          name: 'Rule 1',
          percentage: 5,
          isActive: true,
          belowCostConfirmed: false,
          createdAt: DateTime.utc(2026, 5),
          startsAt: DateTime.utc(2026, 5),
        ),
      );

      final result = await repository.getDiscountRule(ruleId: 'rule-1');

      expect(result.discountRuleId, 'rule-1');
      expect(result.status, 'active');
    });
  });
}
