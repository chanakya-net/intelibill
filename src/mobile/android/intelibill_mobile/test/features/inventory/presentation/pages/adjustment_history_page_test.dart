import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/inventory_adjustment.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/adjustment_history_controller.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/pages/adjustment_history_page.dart';

class _StubAdjustmentHistoryController extends AdjustmentHistoryController {
  _StubAdjustmentHistoryController(this._state);

  final AdjustmentHistoryState _state;

  @override
  AdjustmentHistoryState build() => _state;
}

InventoryAdjustment _makeAdjustment({
  String id = 'adj-1',
  String direction = 'Increase',
  bool isVoided = false,
}) => InventoryAdjustment(
  adjustmentId: id,
  batchId: 'batch-1',
  itemId: 'item-1',
  itemName: 'Rice Premium',
  batchNumber: 'BN-001',
  direction: direction,
  reason: 'Found Stock',
  quantity: 10,
  costImpact: 450,
  performedAt: DateTime(2026, 1, 15, 10, 30),
  performedBy: 'Admin',
  isVoided: isVoided,
);

Widget _buildApp({required AdjustmentHistoryState state}) {
  return ProviderScope(
    overrides: [
      adjustmentHistoryControllerProvider.overrideWith(
        () => _StubAdjustmentHistoryController(state),
      ),
    ],
    child: MaterialApp(
      theme: AppTheme.lightTheme,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: const AdjustmentHistoryPage(),
    ),
  );
}

void main() {
  group('AdjustmentHistoryPage', () {
    testWidgets('renders adjustment cards with item name and direction chip', (
      tester,
    ) async {
      final adj = _makeAdjustment();
      await tester.pumpWidget(
        _buildApp(state: AdjustmentHistoryState(adjustments: [adj])),
      );
      await tester.pump();

      expect(find.text('Rice Premium'), findsOneWidget);
      expect(find.text('Increase'), findsOneWidget);
    });

    testWidgets('renders decrease chip for decrease direction', (tester) async {
      final adj = _makeAdjustment(direction: 'Decrease');
      await tester.pumpWidget(
        _buildApp(state: AdjustmentHistoryState(adjustments: [adj])),
      );
      await tester.pump();

      expect(find.text('Decrease'), findsOneWidget);
    });

    testWidgets('shows empty state text when no adjustments', (tester) async {
      await tester.pumpWidget(_buildApp(state: const AdjustmentHistoryState()));
      await tester.pump();

      expect(find.text('No adjustments found'), findsOneWidget);
    });

    testWidgets('shows error state and retry button on failure', (
      tester,
    ) async {
      await tester.pumpWidget(
        _buildApp(
          state: AdjustmentHistoryState(
            failure: AppException(
              failure: const Failure.server(message: 'error'),
            ),
          ),
        ),
      );
      await tester.pump();

      expect(find.text('Unable to load adjustments'), findsOneWidget);
      expect(find.text('Retry'), findsOneWidget);
    });

    testWidgets('shows bottom CircularProgressIndicator when isLoadingMore', (
      tester,
    ) async {
      final adj = _makeAdjustment();
      await tester.pumpWidget(
        _buildApp(
          state: AdjustmentHistoryState(
            adjustments: [adj],
            isLoadingMore: true,
          ),
        ),
      );
      await tester.pump();

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
    });

    testWidgets('shows voided badge for voided adjustment', (tester) async {
      final adj = _makeAdjustment(isVoided: true);
      await tester.pumpWidget(
        _buildApp(state: AdjustmentHistoryState(adjustments: [adj])),
      );
      await tester.pump();

      expect(find.text('Voided'), findsOneWidget);
    });

    testWidgets('shows loading indicator when isLoading true', (tester) async {
      await tester.pumpWidget(
        _buildApp(state: const AdjustmentHistoryState(isLoading: true)),
      );
      await tester.pump();

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
    });
  });
}
