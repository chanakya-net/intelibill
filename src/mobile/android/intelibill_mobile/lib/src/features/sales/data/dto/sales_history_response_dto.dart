import 'package:freezed_annotation/freezed_annotation.dart';
import 'package:intelibill_mobile/src/features/sales/data/dto/sale_list_item_dto.dart';

part 'sales_history_response_dto.freezed.dart';
part 'sales_history_response_dto.g.dart';

@freezed
sealed class SalesHistorySummaryDto with _$SalesHistorySummaryDto {
  const factory SalesHistorySummaryDto({
    @JsonKey(name: 'periodSales') required double periodSales,
    @JsonKey(name: 'invoiceCount') required int invoiceCount,
    @JsonKey(name: 'refundAmount') required double refundAmount,
  }) = _SalesHistorySummaryDto;

  factory SalesHistorySummaryDto.fromJson(Map<String, dynamic> json) =>
      _$SalesHistorySummaryDtoFromJson(json);
}

@freezed
sealed class SalesHistoryResponseDto with _$SalesHistoryResponseDto {
  const factory SalesHistoryResponseDto({
    @JsonKey(name: 'items') @Default([]) List<SaleListItemDto> items,
    @JsonKey(name: 'totalCount') required int totalCount,
    @JsonKey(name: 'pageNumber') required int pageNumber,
    @JsonKey(name: 'pageSize') required int pageSize,
    @JsonKey(name: 'summary') required SalesHistorySummaryDto summary,
  }) = _SalesHistoryResponseDto;

  factory SalesHistoryResponseDto.fromJson(Map<String, dynamic> json) =>
      _$SalesHistoryResponseDtoFromJson(json);
}
