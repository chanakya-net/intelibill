import 'package:freezed_annotation/freezed_annotation.dart';
import 'package:intelibill_mobile/src/features/inventory/data/dto/item_dto.dart';

part 'item_catalog_response_dto.freezed.dart';
part 'item_catalog_response_dto.g.dart';

@freezed
sealed class ItemCatalogResponseDto with _$ItemCatalogResponseDto {
  const factory ItemCatalogResponseDto({
    @JsonKey(name: 'items') @Default([]) List<ItemDto> items,
    @JsonKey(name: 'totalCount') required int totalCount,
    @JsonKey(name: 'pageNumber') required int pageNumber,
    @JsonKey(name: 'pageSize') required int pageSize,
  }) = _ItemCatalogResponseDto;

  factory ItemCatalogResponseDto.fromJson(Map<String, dynamic> json) =>
      _$ItemCatalogResponseDtoFromJson(json);
}
