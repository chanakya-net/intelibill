import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/shell/menu_visibility.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_orders_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/localization/purchase_order_messages.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_card.dart';

class PurchaseOrdersPage extends ConsumerStatefulWidget {
  const PurchaseOrdersPage({super.key});

  static const pageKey = Key('purchase-orders-page');
  static const countKey = Key('purchase-orders-count');
  static const searchFieldKey = Key('search-field');
  static const dateFromFilterKey = Key('purchase-orders-filter-date-from');
  static const dateToFilterKey = Key('purchase-orders-filter-date-to');
  static const clearFiltersKey = Key('purchase-orders-clear-filters');
  static const newPurchaseOrderFabKey = Key('purchase-orders-new-fab');

  static Key statusFilterKey(PurchaseOrderStatus status) =>
      Key('purchase-orders-filter-status-${status.name}');

  @override
  ConsumerState<PurchaseOrdersPage> createState() => _PurchaseOrdersPageState();
}

class _PurchaseOrdersPageState extends ConsumerState<PurchaseOrdersPage> {
  late final TextEditingController _searchController;
  PurchaseOrderStatus? _selectedStatus;
  DateTime? _dateFrom;
  DateTime? _dateTo;

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
    if (notification.metrics.extentAfter < 500) {
      unawaited(
        ref.read(purchaseOrdersControllerProvider.notifier).loadMore(),
      );
    }
    return false;
  }

  Widget _buildLoadMoreFooter(
    BuildContext context,
    PurchaseOrdersState state,
  ) {
    final l10n = AppLocalizations.of(context)!;
    if (state.isLoadingMore) {
      return const Padding(
        padding: EdgeInsets.all(16),
        child: Center(child: CircularProgressIndicator()),
      );
    }
    if (state.loadMoreFailure != null) {
      return Padding(
        padding: const EdgeInsets.all(16),
        child: Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(
                Icons.error_outline,
                size: 32,
                color: Theme.of(context).colorScheme.error,
              ),
              const SizedBox(height: 8),
              Text(l10n.purchaseOrdersLoadMoreFailure),
              const SizedBox(height: 12),
              FilledButton(
                onPressed: () => ref
                    .read(purchaseOrdersControllerProvider.notifier)
                    .retryLoadMore(),
                child: Text(l10n.purchaseOrdersRetry),
              ),
            ],
          ),
        ),
      );
    }
    if (!state.hasMore && state.items.isNotEmpty) {
      return Padding(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
        child: Text(
          l10n.purchaseOrdersLoadedCount(
            state.items.length,
            state.totalCount,
          ),
          style: Theme.of(context).textTheme.bodySmall?.copyWith(
            color: Theme.of(context).colorScheme.onSurfaceVariant,
          ),
          textAlign: TextAlign.center,
        ),
      );
    }
    return const SizedBox.shrink();
  }

  bool _hasLoadMoreFooter(PurchaseOrdersState state) {
    return state.isLoadingMore ||
        state.loadMoreFailure != null ||
        (!state.isLoading && !state.hasMore && state.items.isNotEmpty);
  }

  Widget _buildRefreshFailureBanner(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Semantics(
        liveRegion: true,
        child: Container(
          padding: const EdgeInsets.all(12),
          decoration: BoxDecoration(
            color: colorScheme.errorContainer,
            borderRadius: BorderRadius.circular(14),
          ),
          child: Row(
            children: [
              Icon(Icons.error_outline, size: 20, color: colorScheme.error),
              const SizedBox(width: 12),
              Expanded(
                child: Text(
                  AppLocalizations.of(context)!.purchaseOrdersRefreshFailed,
                  style: Theme.of(
                    context,
                  ).textTheme.bodySmall?.copyWith(color: colorScheme.error),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final state = ref.watch(purchaseOrdersControllerProvider);
    final authState = ref.watch(authControllerProvider);
    final canCreate = canManagePurchaseOrders(authState.value?.session);
    return Scaffold(
      key: PurchaseOrdersPage.pageKey,
      appBar: AppBar(
        title: Text(l10n.shellManagePurchaseOrders),
        actions: [
          IconButton(
            tooltip: l10n.shellProfile,
            icon: const Icon(Icons.account_circle),
            onPressed: () => context.push(AppRoutes.profile),
          ),
        ],
      ),
      floatingActionButton: canCreate
          ? FloatingActionButton.extended(
              key: PurchaseOrdersPage.newPurchaseOrderFabKey,
              onPressed: () => context.go(AppRoutes.purchaseOrderNew),
              tooltip: l10n.purchaseOrderBuilderTitle,
              icon: const Icon(Icons.add),
              label: Text(l10n.purchaseOrderBuilderTitle),
            )
          : null,
      body: _buildBody(context, state),
    );
  }

  Widget _buildBody(
    BuildContext context,
    PurchaseOrdersState state,
  ) {
    final l10n = AppLocalizations.of(context)!;
    if (state.isInitialLoading) {
      return Center(
        child: Semantics(
          label: l10n.commonLoading,
          liveRegion: true,
          child: const CircularProgressIndicator(),
        ),
      );
    }
    if (state.failure != null) return _FailureView(failure: state.failure!);

    final hasActiveSearch = _searchController.text.trim().isNotEmpty;
    final hasResults = state.items.isNotEmpty;
    final hasActiveFilter =
        _selectedStatus != null ||
        _dateFrom != null ||
        _dateTo != null ||
        hasActiveSearch;

    return NotificationListener<ScrollNotification>(
      onNotification: _onScroll,
      child: RefreshIndicator(
        onRefresh: () =>
            ref.read(purchaseOrdersControllerProvider.notifier).refresh(),
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
                      l10n.shellManagePurchaseOrders,
                      style: Theme.of(context).textTheme.headlineSmall,
                    ),
                    const SizedBox(height: 4),
                    Text(
                      l10n.purchaseOrdersSearchHint,
                      style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                        color: Theme.of(context).colorScheme.onSurfaceVariant,
                      ),
                    ),
                    const SizedBox(height: 12),
                    TextField(
                      controller: _searchController,
                      key: PurchaseOrdersPage.searchFieldKey,
                      decoration: InputDecoration(
                        hintText: l10n.purchaseOrdersSearchHint,
                        prefixIcon: const Icon(Icons.search),
                        suffixIcon: _searchController.text.isEmpty
                            ? null
                            : IconButton(
                                tooltip: l10n.commonClear,
                                icon: const Icon(Icons.clear),
                                onPressed: () {
                                  _searchController.clear();
                                  ref
                                      .read(
                                        purchaseOrdersControllerProvider
                                            .notifier,
                                      )
                                      .updateSearch('');
                                  setState(() {});
                                },
                              ),
                        border: const OutlineInputBorder(),
                        isDense: true,
                      ),
                      onChanged: (value) {
                        ref
                            .read(purchaseOrdersControllerProvider.notifier)
                            .updateSearch(value);
                        setState(() {});
                      },
                    ),
                    const SizedBox(height: 12),
                    SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: Row(
                        children: [
                          Wrap(
                            spacing: 8,
                            children: [
                              for (final status in PurchaseOrderStatus.values)
                                Tooltip(
                                  message: purchaseOrderStatusMessage(
                                    l10n,
                                    status,
                                  ),
                                  child: Semantics(
                                    label: purchaseOrderStatusMessage(
                                      l10n,
                                      status,
                                    ),
                                    selected: _selectedStatus == status,
                                    child: FilterChip(
                                      key: PurchaseOrdersPage.statusFilterKey(
                                        status,
                                      ),
                                      label: Text(
                                        purchaseOrderStatusMessage(
                                          l10n,
                                          status,
                                        ),
                                      ),
                                      selected: _selectedStatus == status,
                                      onSelected: (selected) {
                                        setState(() {
                                          _selectedStatus = selected
                                              ? status
                                              : null;
                                        });
                                        ref
                                            .read(
                                              purchaseOrdersControllerProvider
                                                  .notifier,
                                            )
                                            .updateStatus(_selectedStatus);
                                      },
                                    ),
                                  ),
                                ),
                            ],
                          ),
                          const SizedBox(width: 12),
                          Tooltip(
                            message: l10n.purchaseOrdersFilterDateFrom,
                            child: Semantics(
                              label: l10n.purchaseOrdersFilterDateFrom,
                              button: true,
                              child: OutlinedButton.icon(
                                key: PurchaseOrdersPage.dateFromFilterKey,
                                icon: const Icon(
                                  Icons.calendar_today,
                                  size: 18,
                                ),
                                label: Text(
                                  _dateFrom == null
                                      ? l10n.purchaseOrdersFilterDateFrom
                                      : '${_dateFrom!.year}-${_dateFrom!.month.toString().padLeft(2, '0')}-${_dateFrom!.day.toString().padLeft(2, '0')}',
                                ),
                                onPressed: () => _pickDateFrom(context),
                              ),
                            ),
                          ),
                          const SizedBox(width: 8),
                          Tooltip(
                            message: l10n.purchaseOrdersFilterDateTo,
                            child: Semantics(
                              label: l10n.purchaseOrdersFilterDateTo,
                              button: true,
                              child: OutlinedButton.icon(
                                key: PurchaseOrdersPage.dateToFilterKey,
                                icon: const Icon(
                                  Icons.calendar_today,
                                  size: 18,
                                ),
                                label: Text(
                                  _dateTo == null
                                      ? l10n.purchaseOrdersFilterDateTo
                                      : '${_dateTo!.year}-${_dateTo!.month.toString().padLeft(2, '0')}-${_dateTo!.day.toString().padLeft(2, '0')}',
                                ),
                                onPressed: () => _pickDateTo(context),
                              ),
                            ),
                          ),
                          const SizedBox(width: 8),
                          if (hasActiveFilter)
                            Tooltip(
                              message: l10n.commonClear,
                              child: Semantics(
                                label: l10n.commonClear,
                                button: true,
                                child: ActionChip(
                                  key: PurchaseOrdersPage.clearFiltersKey,
                                  label: Text(l10n.commonClear),
                                  onPressed: _clearFilters,
                                ),
                              ),
                            ),
                        ],
                      ),
                    ),
                    if (state.refreshFailure != null) ...[
                      const SizedBox(height: 8),
                      _buildRefreshFailureBanner(context),
                    ],
                    const SizedBox(height: 8),
                  ],
                ),
              ),
            ),
            if (!hasResults && hasActiveFilter)
              SliverFillRemaining(
                hasScrollBody: false,
                child: _FilteredEmptyView(query: _searchController.text),
              )
            else if (state.isEmpty)
              const SliverFillRemaining(
                hasScrollBody: false,
                child: _EmptyView(),
              )
            else ...[
              SliverToBoxAdapter(
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
                  child: Text(
                    l10n.purchaseOrdersCount(state.totalCount),
                    key: PurchaseOrdersPage.countKey,
                    style: Theme.of(context).textTheme.titleSmall,
                  ),
                ),
              ),
              SliverList(
                delegate: SliverChildBuilderDelegate(
                  (context, index) {
                    final purchaseOrder = state.items[index];
                    return PurchaseOrderCard(
                      purchaseOrder: purchaseOrder,
                    );
                  },
                  childCount: state.items.length,
                ),
              ),
              if (_hasLoadMoreFooter(state))
                SliverToBoxAdapter(
                  child: _buildLoadMoreFooter(context, state),
                ),
              const SliverToBoxAdapter(child: SizedBox(height: 24)),
            ],
          ],
        ),
      ),
    );
  }

  Future<void> _pickDateFrom(BuildContext context) async {
    final picked = await _pickDate(context, _dateFrom);
    if (picked == null) return;
    setState(() => _dateFrom = picked);
    ref
        .read(purchaseOrdersControllerProvider.notifier)
        .updateOrderDateFrom(
          picked,
        );
  }

  Future<void> _pickDateTo(BuildContext context) async {
    final picked = await _pickDate(context, _dateTo);
    if (picked == null) return;
    setState(() => _dateTo = picked);
    ref
        .read(purchaseOrdersControllerProvider.notifier)
        .updateOrderDateTo(picked);
  }

  Future<DateTime?> _pickDate(BuildContext context, DateTime? current) {
    return showDatePicker(
      context: context,
      initialDate: current ?? DateTime.now(),
      firstDate: DateTime(2000),
      lastDate: DateTime.now(),
    );
  }

  void _clearFilters() {
    setState(() {
      _selectedStatus = null;
      _dateFrom = null;
      _dateTo = null;
      _searchController.clear();
    });
    ref.read(purchaseOrdersControllerProvider.notifier).clearFilters();
  }
}

class _FailureView extends ConsumerWidget {
  const _FailureView({required this.failure});

  final Failure failure;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              Icons.cloud_off_outlined,
              size: 48,
              color: theme.colorScheme.onSurfaceVariant,
            ),
            const SizedBox(height: 12),
            Text(
              l10n.purchaseOrdersUnableToLoad,
              style: const TextStyle(
                fontSize: 18,
                fontWeight: FontWeight.w600,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 8),
            Text(
              purchaseOrderFailureMessage(l10n, failure),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 16),
            FilledButton(
              onPressed: () =>
                  ref.read(purchaseOrdersControllerProvider.notifier).retry(),
              child: Text(l10n.purchaseOrdersRetry),
            ),
          ],
        ),
      ),
    );
  }
}

class _EmptyView extends StatelessWidget {
  const _EmptyView();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final l10n = AppLocalizations.of(context)!;
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              Icons.receipt_long_outlined,
              size: 56,
              color: theme.colorScheme.primary.withValues(alpha: 0.7),
            ),
            const SizedBox(height: 16),
            Text(
              l10n.purchaseOrdersEmpty,
              style: theme.textTheme.titleMedium,
              textAlign: TextAlign.center,
            ),
          ],
        ),
      ),
    );
  }
}

class _FilteredEmptyView extends StatelessWidget {
  const _FilteredEmptyView({required this.query});

  final String query;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              Icons.search_off,
              size: 48,
              color: theme.colorScheme.onSurfaceVariant,
            ),
            const SizedBox(height: 12),
            Text(
              query.isEmpty
                  ? l10n.purchaseOrdersFilteredEmpty
                  : l10n.purchaseOrdersQueryEmpty(query),
              textAlign: TextAlign.center,
            ),
          ],
        ),
      ),
    );
  }
}
