import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/dashboard/domain/entities/dashboard.dart';

class DashboardPeriodSelector extends StatelessWidget {
  const DashboardPeriodSelector({
    required this.selectedPeriod,
    required this.onPeriodSelected,
    required this.onCustomRangePressed,
    this.isLoading = false,
    super.key,
  });

  final DashboardPeriod selectedPeriod;
  final ValueChanged<DashboardPeriod> onPeriodSelected;
  final VoidCallback onCustomRangePressed;
  final bool isLoading;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;

    return SegmentedButton<DashboardPeriod>(
      segments: [
        ButtonSegment(
          value: DashboardPeriod.last7,
          label: Text(l10n.dashboardRangeLast7Days),
        ),
        ButtonSegment(
          value: DashboardPeriod.last30,
          label: Text(l10n.dashboardRangeLast30Days),
        ),
        ButtonSegment(
          value: DashboardPeriod.custom,
          label: Text(l10n.dashboardRangeCustom),
          icon: const Icon(Icons.date_range, size: 18),
        ),
      ],
      selected: {selectedPeriod},
      onSelectionChanged: isLoading
          ? null
          : (selection) {
              final period = selection.first;
              if (period == DashboardPeriod.custom) {
                onCustomRangePressed();
                return;
              }
              onPeriodSelected(period);
            },
    );
  }
}
