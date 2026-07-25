import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_query.dart';

class DiscountRuleListCard extends StatelessWidget {
  const DiscountRuleListCard({
    required this.rule,
    required this.onTap,
    super.key,
  });

  final DiscountRule rule;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final l10n = AppLocalizations.of(context)!;
    final statusLabel = _statusLabel(l10n, rule.status);
    final statusColor = _statusColor(theme, rule.status);
    final statusBackground = statusColor.withValues(alpha: 0.12);
    final ruleTypeLabel = _ruleTypeLabel(l10n, rule.ruleType);

    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: InkWell(
        borderRadius: BorderRadius.circular(22),
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Text(
                      rule.name,
                      style: theme.textTheme.titleSmall?.copyWith(
                        fontWeight: FontWeight.w700,
                      ),
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
                  const SizedBox(width: 8),
                  Container(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 8,
                      vertical: 4,
                    ),
                    decoration: BoxDecoration(
                      color: statusBackground,
                      borderRadius: BorderRadius.circular(20),
                    ),
                    child: Text(
                      statusLabel,
                      style: theme.textTheme.labelSmall?.copyWith(
                        color: statusColor,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 10),
              Wrap(
                spacing: 8,
                runSpacing: 6,
                children: [
                  _MetaChip(
                    label: ruleTypeLabel,
                    color: theme.colorScheme.primary,
                  ),
                  if (rule.percentage != null)
                    _MetaChip(
                      label: '${rule.percentage!.toStringAsFixed(1)}%',
                      color: theme.colorScheme.onSurfaceVariant,
                    ),
                ],
              ),
              const SizedBox(height: 10),
              Row(
                children: [
                  const Spacer(),
                  Icon(
                    Icons.chevron_right,
                    color: theme.colorScheme.onSurface.withValues(alpha: 0.4),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
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

  String _ruleTypeLabel(AppLocalizations l10n, String ruleType) {
    switch (ruleType) {
      case DiscountRuleTypeFilter.batchPercentage:
        return l10n.discountsTypeBatch;
      case DiscountRuleTypeFilter.salePercentage:
        return l10n.discountsTypeSalePercent;
      case DiscountRuleTypeFilter.saleThresholdPercentage:
        return l10n.discountsTypeSaleThresholdPercent;
      default:
        return ruleType;
    }
  }
}

Color _statusColor(ThemeData theme, String status) {
  switch (status) {
    case 'active':
      return const Color(0xFF15803D);
    case 'upcoming':
      return theme.colorScheme.primary;
    case 'expired':
      return theme.colorScheme.onSurfaceVariant;
    default:
      return theme.colorScheme.error;
  }
}

class _MetaChip extends StatelessWidget {
  const _MetaChip({required this.label, required this.color});

  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.1),
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: color.withValues(alpha: 0.25)),
      ),
      child: Text(
        label,
        style: Theme.of(context).textTheme.labelSmall?.copyWith(
          color: color,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}
