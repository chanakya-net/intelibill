import 'package:freezed_annotation/freezed_annotation.dart';

part 'app_status_dto.freezed.dart';
part 'app_status_dto.g.dart';

@freezed
sealed class AppStatusDto with _$AppStatusDto {
  const factory AppStatusDto({
    @JsonKey(name: 'statusText') required String statusText,
    @JsonKey(name: 'apiBaseUrl') required String apiBaseUrl,
    @JsonKey(name: 'timestamp') required DateTime timestamp,
    @JsonKey(name: 'environment') String? environment,
  }) = _AppStatusDto;

  factory AppStatusDto.fromJson(Map<String, dynamic> json) =>
      _$AppStatusDtoFromJson(json);
}
