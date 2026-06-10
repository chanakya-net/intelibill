import 'package:equatable/equatable.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_list_item.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sales_history_summary.dart';

class SalesHistoryQuery extends Equatable {
  const SalesHistoryQuery({
    required this.from,
    required this.to,
    this.search,
    this.status,
    this.page = 1,
    this.pageSize = 20,
  });

  final DateTime from;
  final DateTime to;
  final String? search;
  final String? status;
  final int page;
  final int pageSize;

  SalesHistoryQuery copyWith({
    DateTime? from,
    DateTime? to,
    String? search,
    String? status,
    bool clearStatus = false,
    int? page,
    int? pageSize,
  }) {
    return SalesHistoryQuery(
      from: from ?? this.from,
      to: to ?? this.to,
      search: search ?? this.search,
      status: clearStatus ? null : (status ?? this.status),
      page: page ?? this.page,
      pageSize: pageSize ?? this.pageSize,
    );
  }

  @override
  List<Object?> get props => [from, to, search, status, page, pageSize];
}

class SalesHistoryResult extends Equatable {
  const SalesHistoryResult({
    required this.items,
    required this.totalCount,
    required this.pageNumber,
    required this.pageSize,
    required this.summary,
  });

  final List<SaleListItem> items;
  final int totalCount;
  final int pageNumber;
  final int pageSize;
  final SalesHistorySummary summary;

  bool get hasMore => pageNumber * pageSize < totalCount;

  @override
  List<Object?> get props => [items, totalCount, pageNumber, pageSize, summary];
}
