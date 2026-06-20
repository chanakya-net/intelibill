import 'package:equatable/equatable.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note.dart';

class CreditNotesQuery extends Equatable {
  const CreditNotesQuery({
    this.search,
    this.status,
    this.page = 1,
    this.pageSize = 20,
  });

  final String? search;
  final String? status;
  final int page;
  final int pageSize;

  CreditNotesQuery copyWith({
    String? search,
    String? status,
    bool clearStatus = false,
    int? page,
    int? pageSize,
  }) {
    return CreditNotesQuery(
      search: search ?? this.search,
      status: clearStatus ? null : (status ?? this.status),
      page: page ?? this.page,
      pageSize: pageSize ?? this.pageSize,
    );
  }

  @override
  List<Object?> get props => [search, status, page, pageSize];
}

class CreditNotesResult extends Equatable {
  const CreditNotesResult({
    required this.items,
    required this.totalCount,
    required this.pageNumber,
    required this.pageSize,
  });

  final List<CreditNote> items;
  final int totalCount;
  final int pageNumber;
  final int pageSize;

  bool get hasMore => pageNumber * pageSize < totalCount;

  @override
  List<Object?> get props => [items, totalCount, pageNumber, pageSize];
}
