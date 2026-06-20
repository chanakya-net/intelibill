import 'package:intelibill_mobile/src/features/discounts/data/data_sources/discount_remote_data_source.dart';
import 'package:intelibill_mobile/src/features/discounts/data/dto/discount_dto.dart';
import 'package:intelibill_mobile/src/features/discounts/data/dto/discount_preview_request_dto.dart';
import 'package:intelibill_mobile/src/features/discounts/data/dto/discount_request_dto.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_preview.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/repositories/discount_repository.dart';

class DiscountRepositoryImpl implements DiscountRepository {
  const DiscountRepositoryImpl(this._remoteDataSource);

  final DiscountRemoteDataSource _remoteDataSource;

  @override
  Future<DiscountPreview> preview({
    required String name,
    required DiscountType discountType,
    required double discountValue,
    required double? batchPercentage,
  }) async {
    final request = DiscountPreviewRequestDto.fromDomain(
      name: name,
      discountType: discountType,
      discountValue: discountValue,
      batchPercentage: batchPercentage,
    );
    final response = await _remoteDataSource.preview(request);
    return response.toDomain();
  }

  @override
  Future<Discount> create({
    required String name,
    required DiscountType discountType,
    required double discountValue,
    required double? batchPercentage,
  }) async {
    final request = DiscountRequestDto.fromDomain(
      name: name,
      discountType: discountType,
      discountValue: discountValue,
      batchPercentage: batchPercentage,
    );
    final response = await _remoteDataSource.create(request);
    return response.toDomain();
  }

  @override
  Future<Discount> replace({
    required String discountId,
    required String name,
    required DiscountType discountType,
    required double discountValue,
    required double? batchPercentage,
  }) async {
    final request = DiscountRequestDto.fromDomain(
      name: name,
      discountType: discountType,
      discountValue: discountValue,
      batchPercentage: batchPercentage,
    );
    final response = await _remoteDataSource.replace(
      discountId: discountId,
      request: request,
    );
    return response.toDomain();
  }

  @override
  Future<void> disable({required String discountId}) {
    return _remoteDataSource.disable(discountId: discountId);
  }

  @override
  Future<List<Discount>> getAll() async {
    final dtos = await _remoteDataSource.getAll();
    return dtos.map((dto) => dto.toDomain()).toList();
  }
}
