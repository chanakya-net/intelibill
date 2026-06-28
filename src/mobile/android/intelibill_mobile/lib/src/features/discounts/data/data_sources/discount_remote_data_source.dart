import 'package:intelibill_mobile/src/core/network/api_client.dart';
import 'package:intelibill_mobile/src/features/discounts/data/dto/discount_dto.dart';
import 'package:intelibill_mobile/src/features/discounts/data/dto/discount_preview_request_dto.dart';
import 'package:intelibill_mobile/src/features/discounts/data/dto/discount_preview_response_dto.dart';
import 'package:intelibill_mobile/src/features/discounts/data/dto/discount_request_dto.dart';

abstract class DiscountRemoteDataSource {
  Future<DiscountPreviewResponseDto> preview(
    DiscountPreviewRequestDto request,
  );

  Future<DiscountDto> create(DiscountRequestDto request);

  Future<DiscountDto> replace({
    required String discountId,
    required DiscountRequestDto request,
  });

  Future<void> disable({required String discountId});
}

class DiscountRemoteDataSourceImpl implements DiscountRemoteDataSource {
  const DiscountRemoteDataSourceImpl(this._apiClient);

  final ApiClient _apiClient;

  @override
  Future<DiscountPreviewResponseDto> preview(
    DiscountPreviewRequestDto request,
  ) async {
    final response = await _apiClient.post<Map<String, dynamic>>(
      '/discounts/preview',
      data: request.toJson(),
    );
    return DiscountPreviewResponseDto.fromJson(response.data!);
  }

  @override
  Future<DiscountDto> create(DiscountRequestDto request) async {
    final response = await _apiClient.post<Map<String, dynamic>>(
      '/discounts',
      data: request.toJson(),
    );
    return DiscountDto.fromJson(response.data!);
  }

  @override
  Future<DiscountDto> replace({
    required String discountId,
    required DiscountRequestDto request,
  }) async {
    final response = await _apiClient.put<Map<String, dynamic>>(
      '/discounts/$discountId',
      data: request.toJson(),
    );
    return DiscountDto.fromJson(response.data!);
  }

  @override
  Future<void> disable({required String discountId}) async {
    await _apiClient.delete<void>('/discounts/$discountId');
  }
}
