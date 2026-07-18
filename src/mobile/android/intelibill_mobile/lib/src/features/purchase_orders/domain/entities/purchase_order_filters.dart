import 'package:equatable/equatable.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';

class PurchaseOrderFilters extends Equatable {
  const PurchaseOrderFilters({
    this.search,
    this.status,
    this.page = 1,
    this.pageSize = 20,
  });

  final String? search;
  final PurchaseOrderStatus? status;
  final int page;
  final int pageSize;

  PurchaseOrderFilters copyWith({int? page, int? pageSize}) {
    return PurchaseOrderFilters(
      search: search,
      status: status,
      page: page ?? this.page,
      pageSize: pageSize ?? this.pageSize,
    );
  }

  @override
  List<Object?> get props => [search, status, page, pageSize];
}
