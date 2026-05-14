import 'package:intelibill_mobile/src/features/app_status/data/dto/app_status_dto.dart';
import 'package:intelibill_mobile/src/features/app_status/domain/entities/app_status.dart';

class AppStatusMapper {
  static AppStatus toDomain(AppStatusDto dto) {
    return AppStatus(
      statusText: dto.statusText,
      apiBaseUrl: dto.apiBaseUrl,
      timestamp: dto.timestamp,
      environment: dto.environment,
    );
  }

  static AppStatusDto toDto(AppStatus entity) {
    return AppStatusDto(
      statusText: entity.statusText,
      apiBaseUrl: entity.apiBaseUrl,
      timestamp: entity.timestamp,
      environment: entity.environment,
    );
  }
}
