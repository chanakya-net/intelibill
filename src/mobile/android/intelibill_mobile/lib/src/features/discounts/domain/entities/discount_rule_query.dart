import 'package:equatable/equatable.dart';

class DiscountRulesQuery extends Equatable {
  const DiscountRulesQuery({
    this.statusFilter = DiscountRuleStatusFilter.all,
    this.ruleTypeFilter = DiscountRuleTypeFilter.all,
    this.sort = DiscountRuleSort.createdDesc,
    this.search,
    this.page = 1,
    this.pageSize = 20,
  });

  final String statusFilter;
  final String ruleTypeFilter;
  final String sort;
  final String? search;
  final int page;
  final int pageSize;

  DiscountRulesQuery copyWith({
    String? statusFilter,
    String? ruleTypeFilter,
    String? sort,
    String? search,
    bool clearSearch = false,
    int? page,
    int? pageSize,
  }) {
    return DiscountRulesQuery(
      statusFilter: statusFilter ?? this.statusFilter,
      ruleTypeFilter: ruleTypeFilter ?? this.ruleTypeFilter,
      sort: sort ?? this.sort,
      search: clearSearch ? null : (search ?? this.search),
      page: page ?? this.page,
      pageSize: pageSize ?? this.pageSize,
    );
  }

  @override
  List<Object?> get props => [
    statusFilter,
    ruleTypeFilter,
    sort,
    search,
    page,
    pageSize,
  ];
}

class DiscountRuleStatusFilter {
  static const String all = 'all';
  static const String active = 'active';
  static const String upcoming = 'upcoming';
  static const String expired = 'expired';
  static const String disabled = 'disabled';

  static const List<String> values = [
    all,
    active,
    upcoming,
    expired,
    disabled,
  ];
}

class DiscountRuleTypeFilter {
  static const String all = 'all';
  static const String batchPercentage = 'BatchPercentage';
  static const String salePercentage = 'SalePercentage';
  static const String saleThresholdPercentage = 'SaleThresholdPercentage';

  static const List<String> values = [
    all,
    batchPercentage,
    salePercentage,
    saleThresholdPercentage,
  ];
}

class DiscountRuleSort {
  static const String createdDesc = 'created_desc';
  static const String createdAsc = 'created_asc';
  static const String nameAsc = 'name_asc';
  static const String nameDesc = 'name_desc';
  static const String startsAtAsc = 'startsat_asc';
  static const String startsAtDesc = 'startsat_desc';
  static const String status = 'status';

  static const List<String> values = [
    createdDesc,
    createdAsc,
    nameAsc,
    nameDesc,
    startsAtAsc,
    startsAtDesc,
    status,
  ];
}
