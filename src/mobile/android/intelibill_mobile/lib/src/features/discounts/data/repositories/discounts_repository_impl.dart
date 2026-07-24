import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/discounts/data/data_sources/discounts_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/discounts/data/dto/discount_rule_write_dto.dart';
import 'package:intelibill_mobile/src/features/discounts/data/mappers/discount_rule_mapper.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_draft.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_query.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/repositories/discounts_repository.dart';

class DiscountsRepositoryImpl implements DiscountsRepository {
  const DiscountsRepositoryImpl(this._remoteDataSource);

  final DiscountsRemoteDataSource _remoteDataSource;

  @override
  Future<DiscountRulesResult> getDiscountRules(DiscountRulesQuery query) async {
    try {
      final dto = await _remoteDataSource.getDiscountRules(query);
      return DiscountRuleMapper.toResult(dto);
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      throw AppException(failure: Failure.unknown(message: error.toString()));
    }
  }

  @override
  Future<DiscountRule> getDiscountRule({required String ruleId}) async {
    try {
      final dto = await _remoteDataSource.getDiscountRule(ruleId);
      return DiscountRuleMapper.toDomain(dto);
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      throw AppException(failure: Failure.unknown(message: error.toString()));
    }
  }

  @override
  Future<DiscountRulePreview> previewDiscountRule(
    PreviewDiscountRuleInput input,
  ) async {
    try {
      final dto = await _remoteDataSource.previewDiscountRule(
        PreviewDiscountRuleRequestDto(
          ruleType: input.ruleType,
          percentage: input.percentage,
          thresholdAmount: input.thresholdAmount,
          inventoryBatchId: input.inventoryBatchId,
          startsAt: input.startsAt,
          endsAt: input.endsAt,
          belowCostConfirmed: input.belowCostConfirmed,
        ),
      );
      return DiscountRuleMapper.toPreview(dto);
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      throw AppException(failure: Failure.unknown(message: error.toString()));
    }
  }

  @override
  Future<DiscountRule> createDiscountRule(CreateDiscountRuleInput input) async {
    try {
      final dto = await _remoteDataSource.createDiscountRule(
        CreateDiscountRuleRequestDto(
          ruleType: input.ruleType,
          name: input.name,
          description: input.description,
          inventoryBatchId: input.inventoryBatchId,
          percentage: input.percentage,
          thresholdAmount: input.thresholdAmount,
          startsAt: input.startsAt,
          endsAt: input.endsAt,
          belowCostConfirmed: input.belowCostConfirmed,
          belowCostConfirmationReason: input.belowCostConfirmationReason,
        ),
      );
      return DiscountRuleMapper.toDomain(dto);
    } on AppException {
      rethrow;
    } on FormatException catch (error) {
      throw AppException(
        failure: Failure.serialization(message: error.message),
      );
    } catch (error) {
      throw AppException(failure: Failure.unknown(message: error.toString()));
    }
  }
}
