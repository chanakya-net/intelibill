import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_builder_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/localization/purchase_order_messages.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/pages/purchase_order_builder_page.dart';

class _StubBuilderController extends PurchaseOrderBuilderController {
  _StubBuilderController(this.initialState);

  final PurchaseOrderBuilderState initialState;
  int continueCalls = 0;
  int discardRecoveredCalls = 0;
  int discardCalls = 0;
  int retryCalls = 0;

  @override
  PurchaseOrderBuilderState build(String target) => initialState;

  @override
  void continueRecoveredDraft() {
    continueCalls++;
    state = state.copyWith(clearRecoveredDraft: true);
  }

  @override
  Future<bool> discardRecoveredDraftAndReload() async {
    discardRecoveredCalls++;
    state = state.copyWith(clearRecoveredDraft: true);
    return true;
  }

  @override
  Future<bool> discardLocalDraft() async {
    discardCalls++;
    return true;
  }

  @override
  Future<bool> retryStorage() async {
    retryCalls++;
    state = state.copyWith(clearStorageWarning: true);
    return true;
  }
}

void main() {
  Widget buildApp(
    PurchaseOrderBuilderState state, {
    void Function(_StubBuilderController controller)? onCreated,
  }) {
    return ProviderScope(
      overrides: [
        purchaseOrderBuilderControllerProvider('new').overrideWith(() {
          final controller = _StubBuilderController(state);
          onCreated?.call(controller);
          return controller;
        }),
      ],
      child: MaterialApp(
        theme: AppTheme.lightTheme,
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: const PurchaseOrderBuilderPage(target: 'new'),
      ),
    );
  }

  testWidgets('recovered banner Continue keeps editor and dismisses banner', (
    tester,
  ) async {
    _StubBuilderController? controller;
    await tester.pumpWidget(
      buildApp(
        PurchaseOrderBuilderState(
          hasRecoveredDraft: true,
          recoveredDraftUpdatedAt: DateTime.utc(2026, 7, 19),
        ),
        onCreated: (value) => controller = value,
      ),
    );

    expect(
      find.byKey(PurchaseOrderBuilderPage.recoveredDraftBannerKey),
      findsOneWidget,
    );
    await tester.tap(
      find.byKey(PurchaseOrderBuilderPage.continueRecoveredDraftKey),
    );
    await tester.pump();

    expect(controller!.continueCalls, 1);
    expect(
      find.byKey(PurchaseOrderBuilderPage.recoveredDraftBannerKey),
      findsNothing,
    );
    expect(find.byKey(PurchaseOrderBuilderPage.notesFieldKey), findsOneWidget);
  });

  testWidgets('discard recovered draft requires confirmation', (tester) async {
    _StubBuilderController? controller;
    await tester.pumpWidget(
      buildApp(
        PurchaseOrderBuilderState(
          hasRecoveredDraft: true,
          recoveredDraftUpdatedAt: DateTime.utc(2026, 7, 19),
        ),
        onCreated: (value) => controller = value,
      ),
    );

    await tester.tap(
      find.byKey(PurchaseOrderBuilderPage.discardRecoveredDraftKey),
    );
    await tester.pumpAndSettle();
    expect(controller!.discardRecoveredCalls, 0);
    await tester.tap(find.text('Cancel'));
    await tester.pumpAndSettle();
    expect(
      find.byKey(PurchaseOrderBuilderPage.recoveredDraftBannerKey),
      findsOneWidget,
    );

    await tester.tap(
      find.byKey(PurchaseOrderBuilderPage.discardRecoveredDraftKey),
    );
    await tester.pumpAndSettle();
    await tester.tap(
      find.byKey(PurchaseOrderBuilderPage.discardConfirmKey),
    );
    await tester.pumpAndSettle();
    expect(controller!.discardRecoveredCalls, 1);
    expect(
      find.byKey(PurchaseOrderBuilderPage.recoveredDraftBannerKey),
      findsNothing,
    );
  });

  testWidgets('storage warning leaves editor usable and exposes retry', (
    tester,
  ) async {
    _StubBuilderController? controller;
    await tester.pumpWidget(
      buildApp(
        const PurchaseOrderBuilderState(
          storageWarning: PurchaseOrderMessage.draftSaveStorage,
        ),
        onCreated: (value) => controller = value,
      ),
    );

    expect(
      find.byKey(PurchaseOrderBuilderPage.storageWarningKey),
      findsOneWidget,
    );
    expect(find.byKey(PurchaseOrderBuilderPage.notesFieldKey), findsOneWidget);
    expect(
      find.byKey(PurchaseOrderBuilderPage.addItemButtonKey),
      findsOneWidget,
    );
    await tester.tap(find.byKey(PurchaseOrderBuilderPage.retryStorageKey));
    await tester.pump();

    expect(controller!.retryCalls, 1);
    expect(
      find.byKey(PurchaseOrderBuilderPage.storageWarningKey),
      findsNothing,
    );
  });

  testWidgets('ordinary discard confirms then navigates new draft to list', (
    tester,
  ) async {
    _StubBuilderController? controller;
    final router = GoRouter(
      initialLocation: '/builder',
      routes: [
        GoRoute(
          path: '/builder',
          builder: (_, _) => const PurchaseOrderBuilderPage(target: 'new'),
        ),
        GoRoute(
          path: AppRoutes.purchaseOrders,
          builder: (_, _) => const SizedBox(key: Key('purchase-order-list')),
        ),
      ],
    );
    addTearDown(router.dispose);
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          purchaseOrderBuilderControllerProvider('new').overrideWith(() {
            final value = _StubBuilderController(
              const PurchaseOrderBuilderState(),
            );
            controller = value;
            return value;
          }),
        ],
        child: MaterialApp.router(
          theme: AppTheme.lightTheme,
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          routerConfig: router,
        ),
      ),
    );

    await tester.tap(find.byKey(PurchaseOrderBuilderPage.discardButtonKey));
    await tester.pumpAndSettle();
    await tester.tap(
      find.byKey(PurchaseOrderBuilderPage.discardConfirmKey),
    );
    await tester.pumpAndSettle();

    expect(controller!.discardCalls, 1);
    expect(find.byKey(const Key('purchase-order-list')), findsOneWidget);
  });
}
