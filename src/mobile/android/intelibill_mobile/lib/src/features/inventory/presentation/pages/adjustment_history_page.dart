import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/inventory_adjustment.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/adjustment_history_controller.dart';
import 'package:intl/intl.dart';

class AdjustmentHistoryPage extends ConsumerStatefulWidget {
  const AdjustmentHistoryPage({super.key});

  @override
  ConsumerState<AdjustmentHistoryPage> createState() =>
      _AdjustmentHistoryPageState();
}

class _AdjustmentHistoryPageState extends ConsumerState<AdjustmentHistoryPage> {
  bool _onScroll(ScrollNotification notification) {
    if (notification is ScrollEndNotification &&
        notification.metrics.extentAfter == 0.0) {
      unawaited(
        ref.read(adjustmentHistoryControllerProvider.notifier).loadMore(),
      );
    }
    return false;
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(adjustmentHistoryControllerProvider);
    final l10n = AppLocalizations.of(context)!;

    return Scaffold(
      appBar: AppBar(
        title: Text(l10n.inventoryAdjustmentsTitle),
        leading: IconButton(
          icon: const Icon(Icons.arrow_back),
          onPressed: () => context.canPop() ? context.pop() : null,
        ),
        actions: [
          IconButton(
            tooltip: l10n.shellProfile,
            icon: const Icon(Icons.account_circle),
            onPressed: () => context.push(AppRoutes.profile),
          ),
        ],
      ),
      body: _buildBody(context, state, l10n),
    );
  }

  Widget _buildBody(
    BuildContext context,
    AdjustmentHistoryState state,
    AppLocalizations l10n,
  ) {
    final theme = Theme.of(context);

    if (state.isLoading) {
      return Center(
        child: CircularProgressIndicator(color: theme.colorScheme.primary),
      );
    }

    if (state.failure != null && state.adjustments.isEmpty) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(
                Icons.error_outline,
                size: 48,
                color: theme.colorScheme.error,
              ),
              const SizedBox(height: 12),
              Text(
                l10n.inventoryAdjustmentsUnableToLoad,
                style: theme.textTheme.titleMedium,
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 16),
              FilledButton(
                onPressed: () {
                  unawaited(
                    ref
                        .read(adjustmentHistoryControllerProvider.notifier)
                        .refresh(),
                  );
                },
                child: Text(l10n.inventoryAdjustmentsRetry),
              ),
            ],
          ),
        ),
      );
    }

    if (state.adjustments.isEmpty) {
      return RefreshIndicator(
        color: theme.colorScheme.primary,
        onRefresh: ref
            .read(adjustmentHistoryControllerProvider.notifier)
            .refresh,
        child: ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          children: [
            Padding(
              padding: const EdgeInsets.all(32),
              child: Column(
                children: [
                  Icon(
                    Icons.history,
                    size: 56,
                    color: theme.colorScheme.primary.withValues(alpha: 0.7),
                  ),
                  const SizedBox(height: 16),
                  Text(
                    l10n.inventoryAdjustmentsNoAdjustmentsFound,
                    style: theme.textTheme.titleMedium,
                    textAlign: TextAlign.center,
                  ),
                ],
              ),
            ),
          ],
        ),
      );
    }

    return RefreshIndicator(
      color: theme.colorScheme.primary,
      onRefresh: ref.read(adjustmentHistoryControllerProvider.notifier).refresh,
      child: NotificationListener<ScrollNotification>(
        onNotification: _onScroll,
        child: ListView.builder(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: const EdgeInsets.only(top: 8, bottom: 24),
          itemCount: state.adjustments.length + (state.isLoadingMore ? 1 : 0),
          itemBuilder: (context, index) {
            if (index == state.adjustments.length) {
              return Padding(
                padding: const EdgeInsets.symmetric(vertical: 16),
                child: Center(
                  child: CircularProgressIndicator(
                    color: theme.colorScheme.primary,
                  ),
                ),
              );
            }
            return _AdjustmentCard(adjustment: state.adjustments[index]);
          },
        ),
      ),
    );
  }
}

class _AdjustmentCard extends StatelessWidget {
  const _AdjustmentCard({required this.adjustment});

  final InventoryAdjustment adjustment;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final l10n = AppLocalizations.of(context)!;
    final isIncrease = adjustment.direction.toLowerCase() == 'increase';
    final directionLabel = isIncrease
        ? l10n.inventoryAdjustmentsIncrease
        : l10n.inventoryAdjustmentsDecrease;
    final dateFormatted = DateFormat(
      'dd/MM/yyyy HH:mm',
    ).format(adjustment.performedAt.toLocal());
    final quantityDisplay = adjustment.quantity.toStringAsFixed(
      adjustment.quantity.truncateToDouble() == adjustment.quantity ? 0 : 2,
    );
    final costDisplay = adjustment.costImpact.toStringAsFixed(
      adjustment.costImpact.truncateToDouble() == adjustment.costImpact ? 0 : 2,
    );
    final reason = _toSentenceCase(adjustment.reason);
    final initials = _initials(adjustment.itemName);
    final avatarColor = _stableColor(adjustment.itemName, theme);

    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            CircleAvatar(
              backgroundColor: avatarColor,
              child: Text(
                initials,
                style: theme.textTheme.labelLarge?.copyWith(
                  color: Colors.white,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          adjustment.itemName,
                          style: theme.textTheme.titleSmall,
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                      const SizedBox(width: 8),
                      _DirectionChip(
                        label: directionLabel,
                        isIncrease: isIncrease,
                      ),
                      if (adjustment.isVoided) ...[
                        const SizedBox(width: 6),
                        Chip(
                          label: Text(l10n.inventoryAdjustmentsVoided),
                          backgroundColor: theme.colorScheme.errorContainer,
                          labelStyle: theme.textTheme.labelSmall?.copyWith(
                            color: theme.colorScheme.onErrorContainer,
                          ),
                          padding: EdgeInsets.zero,
                          materialTapTargetSize:
                              MaterialTapTargetSize.shrinkWrap,
                        ),
                      ],
                    ],
                  ),
                  const SizedBox(height: 4),
                  Text(
                    '${adjustment.batchNumber} · $reason',
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: theme.colorScheme.onSurfaceVariant,
                    ),
                  ),
                  const SizedBox(height: 6),
                  Wrap(
                    spacing: 8,
                    runSpacing: 4,
                    children: [
                      _MetaChip(label: 'Qty: $quantityDisplay'),
                      _MetaChip(
                        label:
                            '${l10n.inventoryAdjustmentsCostImpactLabel}: '
                            '₹$costDisplay',
                      ),
                    ],
                  ),
                  const SizedBox(height: 4),
                  Text(
                    '${l10n.inventoryAdjustmentsPerformedByLabel}: '
                    '${adjustment.performedBy} · $dateFormatted',
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: theme.colorScheme.outline,
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  String _initials(String name) {
    final parts = name.trim().split(RegExp(r'\s+'));
    if (parts.isEmpty) return '?';
    if (parts.length == 1) {
      return parts[0].isNotEmpty ? parts[0][0].toUpperCase() : '?';
    }
    final first = parts[0].isNotEmpty ? parts[0][0].toUpperCase() : '';
    final last = parts.last.isNotEmpty ? parts.last[0].toUpperCase() : '';
    return '$first$last';
  }

  Color _stableColor(String name, ThemeData theme) {
    final hash = name.codeUnits.fold(0, (acc, c) => acc * 31 + c);
    final colors = [
      theme.colorScheme.primary,
      theme.colorScheme.secondary,
      theme.colorScheme.tertiary,
      Colors.teal,
      Colors.indigo,
      Colors.deepOrange,
    ];
    return colors[hash.abs() % colors.length];
  }

  String _toSentenceCase(String value) {
    if (value.isEmpty) return value;
    return value[0].toUpperCase() + value.substring(1).toLowerCase();
  }
}

class _DirectionChip extends StatelessWidget {
  const _DirectionChip({required this.label, required this.isIncrease});

  final String label;
  final bool isIncrease;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final backgroundColor = isIncrease
        ? theme.colorScheme.secondaryContainer
        : theme.colorScheme.errorContainer;
    final foregroundColor = isIncrease
        ? theme.colorScheme.onSecondaryContainer
        : theme.colorScheme.onErrorContainer;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      decoration: BoxDecoration(
        color: backgroundColor,
        borderRadius: BorderRadius.circular(4),
      ),
      child: Text(
        label,
        style: theme.textTheme.labelSmall?.copyWith(color: foregroundColor),
      ),
    );
  }
}

class _MetaChip extends StatelessWidget {
  const _MetaChip({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
      decoration: BoxDecoration(
        color: theme.colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(4),
      ),
      child: Text(label, style: theme.textTheme.labelSmall),
    );
  }
}
