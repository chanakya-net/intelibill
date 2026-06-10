import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sales_history_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/widgets/sale_detail_sheet.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/widgets/sale_list_card.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/widgets/sales_history_kpi_row.dart';
import 'package:intl/intl.dart';

class SalesHistoryPage extends ConsumerStatefulWidget {
  const SalesHistoryPage({super.key});

  @override
  ConsumerState<SalesHistoryPage> createState() => _SalesHistoryPageState();
}

class _SalesHistoryPageState extends ConsumerState<SalesHistoryPage> {
  late final TextEditingController _searchController;

  @override
  void initState() {
    super.initState();
    _searchController = TextEditingController();
  }

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  bool _onScroll(ScrollNotification notification) {
    if (notification is ScrollEndNotification &&
        notification.metrics.extentAfter == 0.0) {
      unawaited(ref.read(salesHistoryControllerProvider.notifier).loadMore());
    }
    return false;
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(salesHistoryControllerProvider);
    final l10n = AppLocalizations.of(context)!;

    return Scaffold(
      appBar: AppBar(
        title: Text(l10n.shellSalesHistory),
        actions: [
          IconButton(
            tooltip: l10n.shellProfile,
            icon: const Icon(Icons.account_circle),
            onPressed: () => context.push(AppRoutes.profile),
          ),
        ],
      ),
      body: Column(
        children: [
          Expanded(child: _buildBody(context, state, l10n)),
        ],
      ),
    );
  }

  Widget _buildBody(
    BuildContext context,
    SalesHistoryState state,
    AppLocalizations l10n,
  ) {
    if (state.isLoading && state.sales.isEmpty) {
      return const Center(child: CircularProgressIndicator());
    }

    if (state.failure != null && state.sales.isEmpty) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.error_outline, size: 48),
              const SizedBox(height: 12),
              Text(
                l10n.salesHistoryUnableToLoad,
                style: const TextStyle(
                  fontSize: 18,
                  fontWeight: FontWeight.w600,
                ),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 8),
              Text(
                _localizeFailure(l10n, state.failure!),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 16),
              FilledButton(
                onPressed: () {
                  unawaited(
                    ref.read(salesHistoryControllerProvider.notifier).refresh(),
                  );
                },
                child: Text(l10n.salesHistoryRetry),
              ),
            ],
          ),
        ),
      );
    }

    final dateFormat = DateFormat('dd MMM yyyy');
    final rangeLabel = l10n.salesHistoryDateRangeLabel(
      dateFormat.format(state.query.from),
      dateFormat.format(state.query.to),
    );

    return NotificationListener<ScrollNotification>(
      onNotification: _onScroll,
      child: RefreshIndicator(
        onRefresh: () =>
            ref.read(salesHistoryControllerProvider.notifier).refresh(),
        child: CustomScrollView(
          physics: const AlwaysScrollableScrollPhysics(),
          slivers: [
            SliverToBoxAdapter(
              child: Padding(
                padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      l10n.salesHistoryEyebrow,
                      style: Theme.of(context).textTheme.labelSmall?.copyWith(
                        color: const Color(0xFFC8443F),
                        fontWeight: FontWeight.w800,
                        letterSpacing: 1.6,
                      ),
                    ),
                    const SizedBox(height: 4),
                    Text(
                      l10n.salesHistoryTitle,
                      style: Theme.of(context).textTheme.headlineSmall,
                    ),
                    const SizedBox(height: 4),
                    Text(
                      l10n.salesHistorySubtitle,
                      style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                        color: const Color(0xFF766B63),
                      ),
                    ),
                    const SizedBox(height: 16),
                    SalesHistoryKpiRow(summary: state.summary),
                    const SizedBox(height: 16),
                    TextField(
                      controller: _searchController,
                      decoration: InputDecoration(
                        hintText: l10n.salesHistoryControlsSearchPlaceholder,
                        prefixIcon: const Icon(Icons.search),
                        border: const OutlineInputBorder(),
                        isDense: true,
                      ),
                      onChanged: (query) {
                        ref
                            .read(salesHistoryControllerProvider.notifier)
                            .updateSearch(query);
                      },
                    ),
                    const SizedBox(height: 12),
                    SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: Row(
                        children: [
                          for (final filter in _statusFilters(l10n))
                            Padding(
                              padding: const EdgeInsets.only(right: 8),
                              child: FilterChip(
                                label: Text(filter.label),
                                selected: state.statusFilter == filter.value,
                                onSelected: (_) {
                                  unawaited(
                                    ref
                                        .read(
                                          salesHistoryControllerProvider
                                              .notifier,
                                        )
                                        .updateStatusFilter(filter.value),
                                  );
                                },
                              ),
                            ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 12),
                    Row(
                      children: [
                        Expanded(
                          child: OutlinedButton.icon(
                            onPressed: () => _pickDateRange(context, state),
                            icon: const Icon(Icons.date_range, size: 18),
                            label: Text(
                              rangeLabel,
                              overflow: TextOverflow.ellipsis,
                            ),
                          ),
                        ),
                        const SizedBox(width: 8),
                        TextButton(
                          onPressed: () {
                            _searchController.clear();
                            unawaited(
                              ref
                                  .read(salesHistoryControllerProvider.notifier)
                                  .clearFilters(),
                            );
                          },
                          child: Text(l10n.salesHistoryControlsClearFilters),
                        ),
                      ],
                    ),
                    const SizedBox(height: 8),
                  ],
                ),
              ),
            ),
            if (state.sales.isEmpty)
              SliverFillRemaining(
                hasScrollBody: false,
                child: Center(
                  child: Padding(
                    padding: const EdgeInsets.all(32),
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(
                          Icons.receipt_long_outlined,
                          size: 48,
                          color: Theme.of(context).colorScheme.onSurfaceVariant,
                        ),
                        const SizedBox(height: 12),
                        Text(
                          l10n.salesHistoryNoSales,
                          style: const TextStyle(
                            fontSize: 18,
                            fontWeight: FontWeight.w600,
                          ),
                          textAlign: TextAlign.center,
                        ),
                        const SizedBox(height: 8),
                        Text(
                          l10n.salesHistoryNoSalesDescription,
                          textAlign: TextAlign.center,
                        ),
                      ],
                    ),
                  ),
                ),
              )
            else
              SliverList(
                delegate: SliverChildBuilderDelegate(
                  (context, index) {
                    if (index >= state.sales.length) {
                      return const Padding(
                        padding: EdgeInsets.all(16),
                        child: Center(child: CircularProgressIndicator()),
                      );
                    }

                    final sale = state.sales[index];
                    return SaleListCard(
                      sale: sale,
                      onTap: () => unawaited(
                        showSaleDetailSheet(context, sale: sale),
                      ),
                    );
                  },
                  childCount:
                      state.sales.length + (state.isLoadingMore ? 1 : 0),
                ),
              ),
            SliverToBoxAdapter(
              child: Padding(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
                child: Text(
                  l10n.salesHistoryShowingCount(
                    state.sales.length,
                    state.totalCount,
                  ),
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    color: Theme.of(context).colorScheme.onSurfaceVariant,
                  ),
                  textAlign: TextAlign.center,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _pickDateRange(
    BuildContext context,
    SalesHistoryState state,
  ) async {
    final picked = await showDateRangePicker(
      context: context,
      firstDate: DateTime(2020),
      lastDate: DateTime.now().add(const Duration(days: 1)),
      initialDateRange: DateTimeRange(
        start: state.query.from,
        end: state.query.to,
      ),
    );

    if (!mounted || picked == null) return;

    await ref
        .read(salesHistoryControllerProvider.notifier)
        .updateDateRange(
          from: picked.start,
          to: picked.end,
        );
  }
}

List<({String value, String label})> _statusFilters(AppLocalizations l10n) {
  return [
    (value: 'all', label: l10n.salesHistoryStatusAll),
    (value: 'paid', label: l10n.salesHistoryStatusPaid),
    (value: 'refunded', label: l10n.salesHistoryStatusRefunded),
    (value: 'unknown', label: l10n.salesHistoryStatusUnknown),
  ];
}

String _localizeFailure(AppLocalizations l10n, Failure failure) {
  return failure.when(
    validation: (String? message, Map<String, List<String>>? _) =>
        message ?? l10n.salesHistoryErrorGeneric,
    unauthorized: (String? _) => l10n.salesHistoryErrorUnauthorized,
    forbidden: (String? _) => l10n.salesHistoryErrorForbidden,
    notFound: (String? _) => l10n.salesHistoryErrorGeneric,
    server: (String? message, int? _) =>
        message ?? l10n.salesHistoryErrorGeneric,
    network: (String? _) => l10n.salesHistoryErrorNetwork,
    timeout: (String? _) => l10n.salesHistoryErrorTimeout,
    serialization: (String? _) => l10n.salesHistoryErrorGeneric,
    unknown: (String? _) => l10n.salesHistoryErrorGeneric,
  );
}
