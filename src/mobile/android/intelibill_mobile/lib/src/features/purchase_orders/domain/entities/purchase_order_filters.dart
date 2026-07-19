import 'package:equatable/equatable.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';

class PurchaseOrderFilters extends Equatable {
  const PurchaseOrderFilters({
    this.search,
    this.status,
    this.orderDateFrom,
    this.orderDateTo,
    this.page = 1,
    this.pageSize = 20,
  });

  final String? search;
  final PurchaseOrderStatus? status;
  final DateTime? orderDateFrom;
  final DateTime? orderDateTo;
  final int page;
  final int pageSize;

  PurchaseOrderFilters copyWith({
    String? search,
    PurchaseOrderStatus? status,
    DateTime? orderDateFrom,
    DateTime? orderDateTo,
    int? page,
    int? pageSize,
  }) {
    return PurchaseOrderFilters(
      search: search ?? this.search,
      status: status ?? this.status,
      orderDateFrom: orderDateFrom ?? this.orderDateFrom,
      orderDateTo: orderDateTo ?? this.orderDateTo,
      page: page ?? this.page,
      pageSize: pageSize ?? this.pageSize,
    );
  }

  @override
  List<Object?> get props => [
    search,
    status,
    orderDateFrom,
    orderDateTo,
    page,
    pageSize,
  ];
}
