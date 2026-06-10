import 'dart:async';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/dashboard/domain/entities/dashboard.dart';
import 'package:intelibill_mobile/src/features/dashboard/presentation/utils/dashboard_route_mapper.dart';

class DashboardAlertsCard extends StatelessWidget {
  const DashboardAlertsCard({required this.alerts, super.key});

  final List<DashboardAlert> alerts;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              l10n.dashboardAlertsEyebrow,
              style: theme.textTheme.labelMedium?.copyWith(
                color: theme.colorScheme.primary,
              ),
            ),
            const SizedBox(height: 4),
            Text(l10n.dashboardAlertsTitle, style: theme.textTheme.titleMedium),
            const SizedBox(height: 16),
            if (alerts.isEmpty)
              Text(
                l10n.dashboardAlertsEmpty,
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              )
            else
              Column(
                children: [
                  for (final alert in alerts)
                    Padding(
                      padding: const EdgeInsets.only(bottom: 12),
                      child: Container(
                        width: double.infinity,
                        padding: const EdgeInsets.all(12),
                        decoration: BoxDecoration(
                          color: _badgeColor(
                            alert.alertType,
                          ).withValues(alpha: 0.08),
                          borderRadius: BorderRadius.circular(12),
                        ),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Container(
                              padding: const EdgeInsets.symmetric(
                                horizontal: 8,
                                vertical: 4,
                              ),
                              decoration: BoxDecoration(
                                color: _badgeColor(alert.alertType),
                                borderRadius: BorderRadius.circular(999),
                              ),
                              child: Text(
                                alert.title,
                                style: theme.textTheme.labelSmall?.copyWith(
                                  color: Colors.white,
                                  fontWeight: FontWeight.w600,
                                ),
                              ),
                            ),
                            const SizedBox(height: 8),
                            Text(
                              alert.message,
                              style: theme.textTheme.bodyMedium,
                            ),
                            const SizedBox(height: 8),
                            Align(
                              alignment: Alignment.centerRight,
                              child: TextButton(
                                onPressed: () {
                                  final route = mapDashboardActionRoute(
                                    alert.actionRoute,
                                  );
                                  if (route != null) {
                                    unawaited(context.push(route));
                                  }
                                },
                                child: Text(
                                  alert.actionLabel.isNotEmpty
                                      ? alert.actionLabel
                                      : l10n.dashboardAlertsView,
                                ),
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                ],
              ),
          ],
        ),
      ),
    );
  }

  Color _badgeColor(String alertType) {
    switch (alertType) {
      case 'LowStock':
        return const Color(0xFFF27A20);
      case 'ExpiringBatch':
        return const Color(0xFFB85C6D);
      case 'PendingPurchaseOrder':
        return const Color(0xFF3B82F6);
      default:
        return const Color(0xFF64748B);
    }
  }
}
