import 'package:intelibill_mobile/src/features/dashboard/domain/entities/dashboard.dart';

interface class DashboardRepository {
  Future<Dashboard> getDashboard({DateTime? from, DateTime? to}) {
    throw UnimplementedError();
  }
}
