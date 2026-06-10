import 'package:intelibill_mobile/src/features/dashboard/domain/entities/dashboard.dart';
import 'package:intelibill_mobile/src/features/dashboard/domain/repositories/dashboard_repository.dart';

class GetDashboard {
  const GetDashboard(this._repository);

  final DashboardRepository _repository;

  Future<Dashboard> call({DateTime? from, DateTime? to}) {
    return _repository.getDashboard(from: from, to: to);
  }
}
