import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/features/discounts/data/dto/discount_rule_dto.dart';
import 'package:intelibill_mobile/src/features/discounts/data/dto/discount_rules_response_dto.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_query.dart';

interface class DiscountsRemoteDataSource {
  Future<DiscountRulesResponseDto> getDiscountRules(DiscountRulesQuery query) {
    throw UnimplementedError();
  }

  Future<DiscountRuleDto> getDiscountRule(String discountRuleId) {
    throw UnimplementedError();
  }
}

class DiscountsRemoteDataSourceImpl implements DiscountsRemoteDataSource {
  DiscountsRemoteDataSourceImpl(this._apiClient);

  final ApiClient _apiClient;

  static const String _discountsEndpoint = '/discounts';

  @override
  Future<DiscountRulesResponseDto> getDiscountRules(
    DiscountRulesQuery query,
  ) async {
    final response = await _apiClient.get<Map<String, dynamic>>(
      _discountsEndpoint,
      queryParameters: {
        if (query.statusFilter != DiscountRuleStatusFilter.all)
          'status': query.statusFilter,
        if (query.ruleTypeFilter != DiscountRuleTypeFilter.all)
          'ruleType': query.ruleTypeFilter,
        if (query.search?.trim().isNotEmpty == true)
          'search': query.search!.trim(),
        if (query.sort.isNotEmpty) 'sort': query.sort,
        'page': query.page,
        'pageSize': query.pageSize,
      },
    );

    return DiscountRulesResponseDto.fromJson(response.data!);
  }

  @override
  Future<DiscountRuleDto> getDiscountRule(String discountRuleId) async {
    final response = await _apiClient.get<Map<String, dynamic>>(
      '$_discountsEndpoint/$discountRuleId',
    );
    return DiscountRuleDto.fromJson(response.data!);
  }
}
