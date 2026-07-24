import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';

class DashboardQuickActions extends StatelessWidget {
  const DashboardQuickActions({super.key});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    final dividerColor = theme.dividerTheme.color ?? const Color(0xFFFED7AA);

    final actions = <_QuickAction>[
      _QuickAction(
        label: l10n.dashboardQuickActionNewSale,
        icon: Icons.shopping_cart_outlined,
        route: AppRoutes.salesNew,
      ),
      _QuickAction(
        label: l10n.dashboardQuickActionAddInventory,
        icon: Icons.add_box_outlined,
        route: AppRoutes.inventoryBatch,
      ),
      _QuickAction(
        label: l10n.dashboardQuickActionExpenses,
        icon: Icons.account_balance_wallet_outlined,
        route: AppRoutes.expenses,
      ),
      _QuickAction(
        label: l10n.dashboardQuickActionProfitLoss,
        icon: Icons.show_chart,
        route: AppRoutes.profitLoss,
      ),
    ];

    return Wrap(
      spacing: 8,
      runSpacing: 8,
      children: [
        for (final action in actions)
          ActionChip(
            avatar: Icon(
              action.icon,
              size: 18,
              color: theme.colorScheme.primary,
            ),
            label: Text(action.label),
            backgroundColor: theme.colorScheme.surface,
            side: BorderSide(color: dividerColor),
            labelStyle: theme.textTheme.labelLarge?.copyWith(
              color: theme.colorScheme.onSurface,
            ),
            onPressed: () => context.push(action.route),
          ),
      ],
    );
  }
}

class _QuickAction {
  const _QuickAction({
    required this.label,
    required this.icon,
    required this.route,
  });

  final String label;
  final IconData icon;
  final String route;
}
