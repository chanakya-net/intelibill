import 'package:intelibill_mobile/src/features/app_status/domain/entities/app_status.dart';

interface class AppStatusRepository {
  Future<AppStatus> getStatus() {
    throw UnimplementedError();
  }
}
