import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/shell/menu_visibility.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_query.dart';
import 'package:intelibill_mobile/src/features/discounts/presentation/controllers/discounts_controller.dart';
import 'package:intelibill_mobile/src/features/discounts/presentation/widgets/create_discount_rule_sheet.dart';
import 'package:intelibill_mobile/src/features/discounts/presentation/widgets/discount_rule_detail_sheet.dart';
import 'package:intelibill_mobile/src/features/discounts/presentation/widgets/discount_rule_list_card.dart';

class DiscountsPage extends ConsumerStatefulWidget {
  const DiscountsPage({super.key});

  static const createDiscountFabKey = Key('discounts-create-fab');

  @override
  ConsumerState<DiscountsPage> createState() => _DiscountsPageState();
}

class _DiscountsPageState extends ConsumerState<DiscountsPage> {
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
      unawaited(
        ref.read(discountsControllerProvider.notifier).loadMore(),
      );
    }

    return false;
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(discountsControllerProvider);
    final authState = ref.watch(authControllerProvider);
    final l10n = AppLocalizations.of(context)!;
    final canCreate = canManageDiscounts(authState.value?.session);

    return Scaffold(
      appBar: AppBar(
        title: Text(l10n.shellManageDiscounts),
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
              key: DiscountsPage.createDiscountFabKey,
              onPressed: _openCreateDiscountSheet,
              tooltip: l10n.discountsCreateTitle,
              icon: const Icon(Icons.add),
              label: Text(l10n.discountsCreateAction),
            )
          : null,
      body: Column(
        children: [
          Expanded(child: _buildBody(context, state, l10n)),
        ],
      ),
    );
  }

  Future<void> _openCreateDiscountSheet() async {
    final created = await showCreateDiscountRuleSheet(context);
    if (!mounted || created != true) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(AppLocalizations.of(context)!.discountsCreateSuccess),
      ),
    );
  }

  Widget _buildBody(
    BuildContext context,
    DiscountsState state,
    AppLocalizations l10n,
  ) {
    if (state.isLoading && state.rules.isEmpty) {
      return const Center(child: CircularProgressIndicator());
    }

    if (state.failure != null && state.rules.isEmpty) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.error_outline, size: 48),
              const SizedBox(height: 12),
              Text(
                l10n.discountsUnableToLoad,
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
                    ref.read(discountsControllerProvider.notifier).refresh(),
                  );
                },
                child: Text(l10n.discountsRetry),
              ),
            ],
          ),
        ),
      );
    }

    return NotificationListener<ScrollNotification>(
      onNotification: _onScroll,
      child: RefreshIndicator(
        onRefresh: () =>
            ref.read(discountsControllerProvider.notifier).refresh(),
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
                      l10n.shellManageDiscounts,
                      style: Theme.of(context).textTheme.labelSmall?.copyWith(
                        color: const Color(0xFFC8443F),
                        fontWeight: FontWeight.w800,
                        letterSpacing: 1.6,
                      ),
                    ),
                    const SizedBox(height: 4),
                    Text(
                      l10n.discountsTitle,
                      style: Theme.of(context).textTheme.headlineSmall,
                    ),
                    const SizedBox(height: 4),
                    Text(
                      l10n.discountsSubtitle,
                      style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                        color: const Color(0xFF766B63),
                      ),
                    ),
                    const SizedBox(height: 16),
                    TextField(
                      controller: _searchController,
                      decoration: InputDecoration(
                        hintText: l10n.discountsSearchPlaceholder,
                        prefixIcon: const Icon(Icons.search),
                        border: const OutlineInputBorder(),
                        suffixIcon: state.searchQuery.isEmpty
                            ? null
                            : IconButton(
                                tooltip: l10n.discountsClearSearch,
                                icon: const Icon(Icons.clear),
                                onPressed: () {
                                  _searchController.clear();
                                  unawaited(
                                    ref
                                        .read(
                                          discountsControllerProvider.notifier,
                                        )
                                        .clearSearch(),
                                  );
                                },
                              ),
                        isDense: true,
                      ),
                      onChanged: (value) {
                        unawaited(
                          ref
                              .read(discountsControllerProvider.notifier)
                              .updateSearch(value),
                        );
                      },
                    ),
                    const SizedBox(height: 12),
                    SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: Row(
                        children: [
                          for (final status in DiscountRuleStatusFilter.values)
                            Padding(
                              padding: const EdgeInsets.only(right: 8),
                              child: FilterChip(
                                label: Text(_statusLabel(l10n, status)),
                                selected: state.statusFilter == status,
                                onSelected: (_) {
                                  unawaited(
                                    ref
                                        .read(
                                          discountsControllerProvider.notifier,
                                        )
                                        .updateStatusFilter(status),
                                  );
                                },
                              ),
                            ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 12),
                    DropdownButtonFormField<String>(
                      initialValue: state.sort,
                      isExpanded: true,
                      onChanged: (value) {
                        if (value == null) return;
                        unawaited(
                          ref
                              .read(discountsControllerProvider.notifier)
                              .updateSort(value),
                        );
                      },
                      items: DiscountRuleSort.values
                          .map(
                            (value) => DropdownMenuItem<String>(
                              value: value,
                              child: Text(_sortLabel(l10n, value)),
                            ),
                          )
                          .toList(),
                      decoration: InputDecoration(
                        labelText: l10n.discountsSortLabel,
                        border: const OutlineInputBorder(),
                        isDense: true,
                      ),
                    ),
                    const SizedBox(height: 12),
                    SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: Row(
                        children: [
                          for (final type in DiscountRuleTypeFilter.values)
                            Padding(
                              padding: const EdgeInsets.only(right: 8),
                              child: FilterChip(
                                label: Text(_typeLabel(l10n, type)),
                                selected: state.ruleTypeFilter == type,
                                onSelected: (_) {
                                  unawaited(
                                    ref
                                        .read(
                                          discountsControllerProvider.notifier,
                                        )
                                        .updateRuleTypeFilter(type),
                                  );
                                },
                              ),
                            ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 8),
                  ],
                ),
              ),
            ),
            if (state.rules.isEmpty)
              SliverFillRemaining(
                hasScrollBody: false,
                child: Center(
                  child: Padding(
                    padding: const EdgeInsets.all(32),
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(
                          Icons.local_offer_outlined,
                          size: 48,
                          color: Theme.of(context).colorScheme.onSurfaceVariant,
                        ),
                        const SizedBox(height: 12),
                        Text(
                          l10n.discountsNoRules,
                          style: const TextStyle(
                            fontSize: 18,
                            fontWeight: FontWeight.w600,
                          ),
                          textAlign: TextAlign.center,
                        ),
                        const SizedBox(height: 8),
                        Text(
                          l10n.discountsSubtitle,
                          textAlign: TextAlign.center,
                          style: Theme.of(context).textTheme.bodyMedium
                              ?.copyWith(
                                color: Theme.of(
                                  context,
                                ).colorScheme.onSurfaceVariant,
                              ),
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
                    if (index >= state.rules.length) {
                      return const Padding(
                        padding: EdgeInsets.all(16),
                        child: Center(child: CircularProgressIndicator()),
                      );
                    }

                    final rule = state.rules[index];
                    return DiscountRuleListCard(
                      rule: rule,
                      onTap: () async {
                        await ref
                            .read(discountsControllerProvider.notifier)
                            .selectRule(rule.discountRuleId);
                        if (!context.mounted) return;
                        await showDiscountRuleDetailSheet(context);
                      },
                    );
                  },
                  childCount:
                      state.rules.length + (state.isLoadingMore ? 1 : 0),
                ),
              ),
            SliverToBoxAdapter(
              child: Padding(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
                child: Text(
                  l10n.discountsShowingCount(
                    state.rules.length,
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
}

String _statusLabel(AppLocalizations l10n, String status) {
  switch (status) {
    case DiscountRuleStatusFilter.active:
      return l10n.discountsStatusActive;
    case DiscountRuleStatusFilter.upcoming:
      return l10n.discountsStatusUpcoming;
    case DiscountRuleStatusFilter.expired:
      return l10n.discountsStatusExpired;
    case DiscountRuleStatusFilter.disabled:
      return l10n.discountsStatusDisabled;
    default:
      return l10n.discountsStatusAll;
  }
}

String _sortLabel(AppLocalizations l10n, String sort) {
  switch (sort) {
    case DiscountRuleSort.nameAsc:
      return l10n.discountsSortNameAsc;
    case DiscountRuleSort.nameDesc:
      return l10n.discountsSortNameDesc;
    case DiscountRuleSort.createdAsc:
      return l10n.discountsSortCreatedAsc;
    case DiscountRuleSort.startsAtAsc:
      return l10n.discountsSortStartsAtAsc;
    case DiscountRuleSort.startsAtDesc:
      return l10n.discountsSortStartsAtDesc;
    case DiscountRuleSort.status:
      return l10n.discountsSortStatus;
    default:
      return l10n.discountsSortCreatedDesc;
  }
}

String _typeLabel(AppLocalizations l10n, String type) {
  switch (type) {
    case DiscountRuleTypeFilter.batchPercentage:
      return l10n.discountsTypeBatch;
    case DiscountRuleTypeFilter.salePercentage:
      return l10n.discountsTypeSalePercent;
    case DiscountRuleTypeFilter.saleThresholdPercentage:
      return l10n.discountsTypeSaleThresholdPercent;
    default:
      return l10n.discountsTypeAll;
  }
}

String _localizeFailure(AppLocalizations l10n, Failure failure) {
  return failure.when(
    validation: (String? message, Map<String, List<String>>? _) =>
        message ?? l10n.discountsErrorGeneric,
    unauthorized: (String? _) => l10n.discountsErrorUnauthorized,
    forbidden: (String? _) => l10n.discountsErrorForbidden,
    notFound: (String? _) => l10n.discountsErrorGeneric,
    server: (String? message, int? _) => message ?? l10n.discountsErrorGeneric,
    network: (String? _) => l10n.discountsErrorNetwork,
    timeout: (String? _) => l10n.discountsErrorTimeout,
    serialization: (String? _) => l10n.discountsErrorGeneric,
    unknown: (String? _) => l10n.discountsErrorGeneric,
  );
}
