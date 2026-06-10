// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'sales_history_response_dto.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

_SalesHistorySummaryDto _$SalesHistorySummaryDtoFromJson(
  Map<String, dynamic> json,
) => _SalesHistorySummaryDto(
  periodSales: (json['periodSales'] as num).toDouble(),
  invoiceCount: (json['invoiceCount'] as num).toInt(),
  refundAmount: (json['refundAmount'] as num).toDouble(),
);

Map<String, dynamic> _$SalesHistorySummaryDtoToJson(
  _SalesHistorySummaryDto instance,
) => <String, dynamic>{
  'periodSales': instance.periodSales,
  'invoiceCount': instance.invoiceCount,
  'refundAmount': instance.refundAmount,
};

_SalesHistoryResponseDto _$SalesHistoryResponseDtoFromJson(
  Map<String, dynamic> json,
) => _SalesHistoryResponseDto(
  items:
      (json['items'] as List<dynamic>?)
          ?.map((e) => SaleListItemDto.fromJson(e as Map<String, dynamic>))
          .toList() ??
      const [],
  totalCount: (json['totalCount'] as num).toInt(),
  pageNumber: (json['pageNumber'] as num).toInt(),
  pageSize: (json['pageSize'] as num).toInt(),
  summary: SalesHistorySummaryDto.fromJson(
    json['summary'] as Map<String, dynamic>,
  ),
);

Map<String, dynamic> _$SalesHistoryResponseDtoToJson(
  _SalesHistoryResponseDto instance,
) => <String, dynamic>{
  'items': instance.items,
  'totalCount': instance.totalCount,
  'pageNumber': instance.pageNumber,
  'pageSize': instance.pageSize,
  'summary': instance.summary,
};
