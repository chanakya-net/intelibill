import 'package:equatable/equatable.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_list_item.dart';

class PurchaseOrderPage extends Equatable {
  const PurchaseOrderPage({
    required this.items,
    required this.totalCount,
    required this.pageNumber,
    required this.pageSize,
  });

  final List<PurchaseOrderListItem> items;
  final int totalCount;
  final int pageNumber;
  final int pageSize;

  bool get hasMore => pageNumber * pageSize < totalCount;

  @override
  List<Object?> get props => [items, totalCount, pageNumber, pageSize];
}
