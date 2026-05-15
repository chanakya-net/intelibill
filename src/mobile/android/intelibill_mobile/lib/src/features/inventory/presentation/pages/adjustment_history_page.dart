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

class _AdjustmentHistoryPageState
    extends ConsumerState<AdjustmentHistoryPage> {
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
        title: Text(l10n.shellInventoryAdjustments),
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
    if (state.isLoading) {
      return const Center(child: CircularProgressIndicator());
    }

    if (state.failure != null && state.adjustments.isEmpty) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.error_outline, size: 48),
              const SizedBox(height: 12),
              Text(
                l10n.inventoryAdjustmentsUnableToLoad,
                style: const TextStyle(
                  fontSize: 18,
                  fontWeight: FontWeight.w600,
                ),
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
        onRefresh: ref
            .read(adjustmentHistoryControllerProvider.notifier)
            .refresh,
        child: ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          children: [
            Padding(
              padding: const EdgeInsets.all(32),
              child: Center(
                child: Text(
                  l10n.inventoryAdjustmentsNoAdjustmentsFound,
                  style: const TextStyle(fontSize: 16),
                ),
              ),
            ),
          ],
        ),
      );
    }

    return RefreshIndicator(
      onRefresh: ref
          .read(adjustmentHistoryControllerProvider.notifier)
          .refresh,
      child: NotificationListener<ScrollNotification>(
        onNotification: _onScroll,
        child: ListView.builder(
          physics: const AlwaysScrollableScrollPhysics(),
          itemCount: state.adjustments.length + (state.isLoadingMore ? 1 : 0),
          itemBuilder: (context, index) {
            if (index == state.adjustments.length) {
              return const Padding(
                padding: EdgeInsets.symmetric(vertical: 16),
                child: Center(child: CircularProgressIndicator()),
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
    final directionBg = isIncrease
        ? theme.colorScheme.secondary
        : theme.colorScheme.error;
    final directionLabel = isIncrease
        ? l10n.inventoryAdjustmentsIncrease
        : l10n.inventoryAdjustmentsDecrease;
    final dateFormatted =
        DateFormat('dd/MM/yyyy HH:mm').format(adjustment.performedAt);
    final quantityDisplay = adjustment.quantity
        .toStringAsFixed(
          adjustment.quantity.truncateToDouble() == adjustment.quantity ? 0 : 2,
        );
    final costDisplay = adjustment.costImpact
        .toStringAsFixed(
          adjustment.costImpact.truncateToDouble() == adjustment.costImpact
              ? 0
              : 2,
        );
    final reason = _toSentenceCase(adjustment.reason);

    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: Padding(
        padding: const EdgeInsets.all(12),
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
                _Chip(
                  label: directionLabel,
                  backgroundColor: directionBg,
                  foregroundColor: Colors.white,
                ),
                if (adjustment.isVoided) ...[
                  const SizedBox(width: 6),
                  _Chip(
                    label: l10n.inventoryAdjustmentsVoided,
                    backgroundColor: theme.colorScheme.errorContainer,
                    foregroundColor: theme.colorScheme.onErrorContainer,
                  ),
                ],
              ],
            ),
            const SizedBox(height: 4),
            Text(
              '${adjustment.batchNumber} · $reason',
              style: theme.textTheme.bodySmall,
            ),
            const SizedBox(height: 4),
            Text(
              '$quantityDisplay  ·  '
              '${l10n.inventoryAdjustmentsCostImpactLabel}: $costDisplay',
              style: theme.textTheme.bodySmall,
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
    );
  }

  String _toSentenceCase(String value) {
    if (value.isEmpty) return value;
    return value[0].toUpperCase() + value.substring(1).toLowerCase();
  }
}

class _Chip extends StatelessWidget {
  const _Chip({
    required this.label,
    required this.backgroundColor,
    required this.foregroundColor,
  });

  final String label;
  final Color backgroundColor;
  final Color foregroundColor;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
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
