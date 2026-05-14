import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/controllers/suppliers_controller.dart';
import 'package:intelibill_mobile/src/features/suppliers/presentation/pages/suppliers_page.dart';

class _StubSuppliersController extends SuppliersController {
  _StubSuppliersController(this._state);

  final SuppliersState _state;

  @override
  SuppliersState build() => _state;
}

const _loadedState = SuppliersState(
  suppliers: [
    Supplier(
      supplierId: 'sup-1',
      name: 'Alpha Supplies',
      contactPersonName: 'Alice',
      city: 'Mumbai',
      state: 'Maharashtra',
      isSystem: false,
      isActive: true,
      isPreferred: true,
      balanceDue: 100,
    ),
    Supplier(
      supplierId: 'sup-2',
      name: 'System Supplier',
      contactPersonName: 'Root',
      city: 'Remote',
      isSystem: true,
      isActive: true,
      isPreferred: false,
      balanceDue: 0,
    ),
    Supplier(
      supplierId: 'sup-3',
      name: 'Beta Traders',
      contactPersonName: 'Bob',
      city: 'Pune',
      state: 'Maharashtra',
      isSystem: false,
      isActive: false,
      isPreferred: false,
      balanceDue: 0,
    ),
  ],
);

Widget _buildApp(SuppliersState state) {
  return ProviderScope(
    overrides: [
      suppliersControllerProvider.overrideWith(
        () => _StubSuppliersController(state),
      ),
    ],
    child: const MaterialApp(
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: SuppliersPage(),
    ),
  );
}

Widget _buildAppWithOverrides({required SuppliersController controller}) {
  return ProviderScope(
    overrides: [
      suppliersControllerProvider.overrideWith(() => controller),
    ],
    child: const MaterialApp(
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: SuppliersPage(),
    ),
  );
}

void main() {
  group('SuppliersPage', () {
    testWidgets('shows loading indicator when isLoading is true', (
      tester,
    ) async {
      await tester.pumpWidget(
        _buildApp(const SuppliersState(isLoading: true)),
      );

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
    });

    testWidgets('shows empty state when supplier list is empty', (
      tester,
    ) async {
      await tester.pumpWidget(_buildApp(const SuppliersState()));
      await tester.pumpAndSettle();

      expect(find.text('No suppliers found'), findsOneWidget);
    });

    testWidgets('shows error state with retry button when error is set', (
      tester,
    ) async {
      await tester.pumpWidget(
        _buildApp(
          const SuppliersState(failure: NetworkFailure()),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Unable to load suppliers'), findsOneWidget);
      expect(
        find.text('Unable to connect. Please check your network.'),
        findsOneWidget,
      );
      expect(find.widgetWithText(FilledButton, 'Retry'), findsOneWidget);
    });

    testWidgets('shows supplier cards when suppliers are loaded', (
      tester,
    ) async {
      await tester.pumpWidget(_buildApp(_loadedState));
      await tester.pumpAndSettle();

      expect(find.text('Alpha Supplies'), findsOneWidget);
      expect(find.text('Alice'), findsOneWidget);
      expect(find.text('Beta Traders'), findsOneWidget);
      expect(find.text('Bob'), findsOneWidget);
      expect(find.textContaining('100.00'), findsOneWidget);
    });

    testWidgets('does not show system suppliers in list', (tester) async {
      await tester.pumpWidget(_buildApp(_loadedState));
      await tester.pumpAndSettle();

      expect(find.text('System Supplier'), findsNothing);
    });

    testWidgets('shows title from localization', (tester) async {
      await tester.pumpWidget(_buildApp(_loadedState));
      await tester.pumpAndSettle();

      expect(find.text('Suppliers'), findsOneWidget);
    });

    testWidgets('shows search field', (tester) async {
      await tester.pumpWidget(_buildApp(_loadedState));
      await tester.pumpAndSettle();

      expect(find.byType(TextField), findsOneWidget);
    });

    testWidgets('shows RefreshIndicator in loaded state', (tester) async {
      await tester.pumpWidget(_buildApp(_loadedState));
      await tester.pumpAndSettle();

      expect(find.byType(RefreshIndicator), findsOneWidget);
    });

    testWidgets('pull-to-refresh calls refresh on controller', (tester) async {
      var refreshCount = 0;

      final controller = _CountingRefreshController(_loadedState, () {
        refreshCount++;
      });

      await tester.pumpWidget(
        _buildAppWithOverrides(controller: controller),
      );
      await tester.pumpAndSettle();

      await tester.fling(
        find.byType(ListView),
        const Offset(0, 300),
        1000,
      );
      await tester.pumpAndSettle();

      expect(refreshCount, greaterThanOrEqualTo(1));
    });

    testWidgets('filters shown suppliers based on search query', (
      tester,
    ) async {
      await tester.pumpWidget(_buildApp(_loadedState));
      await tester.pumpAndSettle();

      await tester.enterText(find.byType(TextField), 'pune');
      await tester.pump();

      expect(find.text('Beta Traders'), findsOneWidget);
      expect(find.text('Alpha Supplies'), findsNothing);
    });
  });
}

class _CountingRefreshController extends SuppliersController {
  _CountingRefreshController(this._state, this._onRefresh);

  final SuppliersState _state;
  final VoidCallback _onRefresh;

  @override
  SuppliersState build() => _state;

  @override
  Future<void> refresh() async {
    _onRefresh();
  }
}
