import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/shell/menu_visibility.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/dashboard/domain/entities/dashboard.dart';
import 'package:intelibill_mobile/src/features/dashboard/presentation/controllers/dashboard_controller.dart';
import 'package:intelibill_mobile/src/features/dashboard/presentation/widgets/dashboard_alerts_card.dart';
import 'package:intelibill_mobile/src/features/dashboard/presentation/widgets/dashboard_kpi_grid.dart';
import 'package:intelibill_mobile/src/features/dashboard/presentation/widgets/dashboard_latest_sales_card.dart';
import 'package:intelibill_mobile/src/features/dashboard/presentation/widgets/dashboard_period_selector.dart';
import 'package:intelibill_mobile/src/features/dashboard/presentation/widgets/dashboard_quick_actions.dart';
import 'package:intelibill_mobile/src/features/dashboard/presentation/widgets/dashboard_sales_chart.dart';
import 'package:intl/intl.dart';

class DashboardPage extends ConsumerWidget {
  const DashboardPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context)!;
    final authState = ref.watch(authControllerProvider).value;
    final canViewDashboard = isOwnerOrManager(authState?.session);

    return Scaffold(
      appBar: AppBar(
        title: Text(l10n.shellDashboard),
        actions: [
          IconButton(
            tooltip: l10n.shellProfile,
            icon: const Icon(Icons.account_circle),
            onPressed: () => context.push(AppRoutes.profile),
          ),
        ],
      ),
      body: canViewDashboard
          ? const _AuthorizedDashboardBody()
          : const SizedBox.shrink(),
    );
  }
}

class _AuthorizedDashboardBody extends ConsumerWidget {
  const _AuthorizedDashboardBody();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context)!;
    final authState = ref.watch(authControllerProvider).value;
    final dashboardState = ref.watch(dashboardControllerProvider);
    final shopName = activeShopForSession(authState?.session)?.shopName ?? '';

    return _DashboardBody(
      dashboardState: dashboardState,
      shopName: shopName,
      l10n: l10n,
      onRefresh: () => ref.read(dashboardControllerProvider.notifier).refresh(),
      onPeriodSelected: (period) =>
          ref.read(dashboardControllerProvider.notifier).setPeriod(period),
      onCustomRangePressed: () => _pickCustomRange(context, ref),
    );
  }

  Future<void> _pickCustomRange(BuildContext context, WidgetRef ref) async {
    final now = DateTime.now();
    final range = await showDateRangePicker(
      context: context,
      firstDate: DateTime(now.year - 5),
      lastDate: now,
      initialDateRange: DateTimeRange(
        start: now.subtract(const Duration(days: 29)),
        end: now,
      ),
    );

    if (range == null || !context.mounted) {
      return;
    }

    await ref
        .read(dashboardControllerProvider.notifier)
        .setCustomRange(from: range.start, to: range.end);
  }
}

class _DashboardBody extends StatelessWidget {
  const _DashboardBody({
    required this.dashboardState,
    required this.shopName,
    required this.l10n,
    required this.onRefresh,
    required this.onPeriodSelected,
    required this.onCustomRangePressed,
  });

  final DashboardState dashboardState;
  final String shopName;
  final AppLocalizations l10n;
  final Future<void> Function() onRefresh;
  final ValueChanged<DashboardPeriod> onPeriodSelected;
  final VoidCallback onCustomRangePressed;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    if (dashboardState.isLoading && dashboardState.dashboard == null) {
      return Center(
        child: CircularProgressIndicator(color: theme.colorScheme.primary),
      );
    }

    if (dashboardState.failure != null && dashboardState.dashboard == null) {
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
                l10n.dashboardUnableToLoad,
                style: theme.textTheme.titleMedium,
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 8),
              Text(
                _localizeFailure(l10n, dashboardState.failure!),
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 16),
              FilledButton(
                onPressed: () => unawaited(onRefresh()),
                child: Text(l10n.dashboardRetry),
              ),
            ],
          ),
        ),
      );
    }

    final dashboard = dashboardState.dashboard;
    if (dashboard == null) {
      return Center(
        child: Text(
          l10n.dashboardNoData,
          style: theme.textTheme.bodyLarge?.copyWith(
            color: theme.colorScheme.onSurfaceVariant,
          ),
          textAlign: TextAlign.center,
        ),
      );
    }

    return RefreshIndicator(
      color: theme.colorScheme.primary,
      onRefresh: onRefresh,
      child: ListView(
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.fromLTRB(16, 12, 16, 24),
        children: [
          Text(
            _greeting(l10n, shopName),
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          const SizedBox(height: 4),
          Text(
            l10n.dashboardSubtitle,
            style: Theme.of(context).textTheme.bodyMedium?.copyWith(
              color: Theme.of(context).colorScheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: 8),
          Text(
            _formatDateRange(dashboard.startDate, dashboard.endDate),
            style: Theme.of(context).textTheme.labelLarge,
          ),
          const SizedBox(height: 16),
          DashboardPeriodSelector(
            selectedPeriod: dashboardState.selectedPeriod,
            onPeriodSelected: onPeriodSelected,
            onCustomRangePressed: onCustomRangePressed,
            isLoading: dashboardState.isLoading,
          ),
          if (dashboardState.selectedPeriod == DashboardPeriod.custom &&
              dashboardState.customFrom != null &&
              dashboardState.customTo != null) ...[
            const SizedBox(height: 8),
            Text(
              '${l10n.dashboardRangeFrom}: '
              '${_formatDateOnly(dashboardState.customFrom!)}  '
              '${l10n.dashboardRangeTo}: '
              '${_formatDateOnly(dashboardState.customTo!)}',
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ],
          if (dashboardState.isLoading) ...[
            const SizedBox(height: 24),
            Center(
              child: CircularProgressIndicator(
                color: theme.colorScheme.primary,
              ),
            ),
          ] else ...[
            const SizedBox(height: 20),
            DashboardKpiGrid(dashboard: dashboard),
            const SizedBox(height: 20),
            DashboardSalesChart(dashboard: dashboard),
            const SizedBox(height: 20),
            DashboardLatestSalesCard(latestSales: dashboard.latestSales),
            const SizedBox(height: 12),
            DashboardAlertsCard(alerts: dashboard.alerts),
            const SizedBox(height: 20),
            Text(
              l10n.dashboardQuickActionsTitle,
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 12),
            const DashboardQuickActions(),
          ],
        ],
      ),
    );
  }

  String _greeting(AppLocalizations l10n, String shopName) {
    final hour = DateTime.now().hour;
    if (hour < 12) {
      return l10n.dashboardGreetingMorning(shopName);
    }
    if (hour < 17) {
      return l10n.dashboardGreetingAfternoon(shopName);
    }
    return l10n.dashboardGreetingEvening(shopName);
  }

  String _formatDateRange(DateTime start, DateTime end) {
    final formatter = DateFormat('d MMM yyyy');
    return '${formatter.format(start)} – ${formatter.format(end)}';
  }

  String _formatDateOnly(DateTime date) {
    return DateFormat('d MMM yyyy').format(date);
  }
}

String _localizeFailure(AppLocalizations l10n, Failure failure) {
  return failure.when(
    validation: (String? message, Map<String, List<String>>? _) =>
        message ?? l10n.dashboardErrorGeneric,
    unauthorized: (String? _) => l10n.dashboardErrorUnauthorized,
    forbidden: (String? _) => l10n.dashboardErrorForbidden,
    notFound: (String? _) => l10n.dashboardErrorGeneric,
    server: (String? message, int? _) => message ?? l10n.dashboardErrorGeneric,
    network: (String? _) => l10n.dashboardErrorNetwork,
    timeout: (String? _) => l10n.dashboardErrorTimeout,
    serialization: (String? _) => l10n.dashboardErrorGeneric,
    unknown: (String? _) => l10n.dashboardErrorGeneric,
  );
}
