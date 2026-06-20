import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule.dart';
import 'package:intelibill_mobile/src/features/discounts/presentation/controllers/discounts_controller.dart';
import 'package:intl/intl.dart';

Future<void> showDiscountRuleDetailSheet(BuildContext context) {
  return showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    useSafeArea: true,
    builder: (context) => const DiscountRuleDetailSheet(),
  );
}

class DiscountRuleDetailSheet extends ConsumerWidget {
  const DiscountRuleDetailSheet({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(discountsControllerProvider);
    final l10n = AppLocalizations.of(context)!;

    if (state.isDetailLoading) {
      return const SizedBox(
        height: 200,
        child: Center(child: CircularProgressIndicator()),
      );
    }

    if (state.detailFailure != null) {
      return Padding(
        padding: const EdgeInsets.all(24),
        child: Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(
                Icons.error_outline,
                size: 48,
                color: Theme.of(context).colorScheme.error,
              ),
              const SizedBox(height: 12),
              Text(
                l10n.discountsUnableToLoadDetail,
                style: const TextStyle(
                  fontSize: 18,
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 8),
              Text(
                _localizeFailure(l10n, state.detailFailure!),
                textAlign: TextAlign.center,
              ),
            ],
          ),
        ),
      );
    }

    final rule = state.selectedRule;
    if (rule == null) {
      return const SizedBox(
        height: 120,
        child: Center(child: Text('No discount selected')),
      );
    }

    final dateFormat = DateFormat('dd MMM yyyy, h:mm a');
    final rows = <_DetailRow>[
      _DetailRow(l10n.discountsDetailRuleLabel, rule.name),
      _DetailRow(l10n.discountsRuleTypeLabel, rule.ruleType),
      _DetailRow(l10n.discountsStatusLabel, _statusLabel(l10n, rule.status)),
      if (rule.percentage != null)
        _DetailRow(
          l10n.discountsPercentageLabel,
          '${rule.percentage!.toStringAsFixed(1)}%',
        ),
      if (rule.thresholdAmount != null)
        _DetailRow(
          l10n.discountsThresholdLabel,
          rule.thresholdAmount.toString(),
        ),
      if (rule.inventoryBatchId != null)
        _DetailRow(l10n.discountsInventoryBatchIdLabel, rule.inventoryBatchId!),
      if (rule.description != null && rule.description!.trim().isNotEmpty)
        _DetailRow(l10n.discountsDescriptionLabel, rule.description!),
      if (rule.disabledReason != null && rule.disabledReason!.trim().isNotEmpty)
        _DetailRow(l10n.discountsDisabledReasonLabel, rule.disabledReason!),
      if (rule.belowCostConfirmationReason != null &&
          rule.belowCostConfirmationReason!.trim().isNotEmpty)
        _DetailRow(
          l10n.discountsBelowCostReasonLabel,
          rule.belowCostConfirmationReason!,
        ),
      _DetailRow(
        l10n.discountsStartsAtLabel,
        rule.startsAt == null
            ? l10n.discountsNotSet
            : dateFormat.format(rule.startsAt!),
      ),
      _DetailRow(
        l10n.discountsEndsAtLabel,
        rule.endsAt == null
            ? l10n.discountsNotSet
            : dateFormat.format(rule.endsAt!),
      ),
      _DetailRow(
        l10n.discountsCreatedAtLabel,
        dateFormat.format(rule.createdAt.toLocal()),
      ),
      if (rule.updatedAt != null)
        _DetailRow(
          l10n.discountsUpdatedAtLabel,
          dateFormat.format(rule.updatedAt!.toLocal()),
        ),
      if (rule.disabledAt != null)
        _DetailRow(
          l10n.discountsDisabledAtLabel,
          dateFormat.format(rule.disabledAt!.toLocal()),
        ),
      if (rule.replacesRuleId != null)
        _DetailRow(l10n.discountsReplacesRuleLabel, rule.replacesRuleId!),
      if (rule.replacedByRuleId != null)
        _DetailRow(l10n.discountsReplacedByRuleLabel, rule.replacedByRuleId!),
    ];

    return SafeArea(
      child: ListView(
        shrinkWrap: true,
        padding: const EdgeInsets.fromLTRB(16, 4, 16, 24),
        children: [
          Text(
            l10n.discountsDetailTitle,
            style: Theme.of(context).textTheme.titleLarge,
          ),
          const SizedBox(height: 16),
          for (final row in rows) ...[
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 6),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Text(
                      row.label,
                      style: TextStyle(
                        color: Theme.of(context).colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ),
                  Expanded(
                    child: Text(
                      row.value,
                      style: const TextStyle(fontWeight: FontWeight.w600),
                    ),
                  ),
                ],
              ),
            ),
            const Divider(height: 0),
          ],
        ],
      ),
    );
  }
}

String _statusLabel(AppLocalizations l10n, String status) {
  switch (status) {
    case 'active':
      return l10n.discountsStatusActive;
    case 'upcoming':
      return l10n.discountsStatusUpcoming;
    case 'expired':
      return l10n.discountsStatusExpired;
    default:
      return l10n.discountsStatusDisabled;
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

class _DetailRow {
  const _DetailRow(this.label, this.value);

  final String label;
  final String value;
}
