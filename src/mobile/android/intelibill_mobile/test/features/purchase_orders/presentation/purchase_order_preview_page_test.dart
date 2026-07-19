import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_preview_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/pages/purchase_order_preview_page.dart';

void main() {
  testWidgets('localizes preview load failure without exposing diagnostics', (
    tester,
  ) async {
    const raw = 'sensitive server diagnostic';
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          purchaseOrderPreviewControllerProvider('po-1').overrideWithValue(
            const PurchaseOrderPreviewState(
              failure: Failure.server(message: raw, statusCode: 500),
            ),
          ),
        ],
        child: const MaterialApp(
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          home: PurchaseOrderPreviewPage(purchaseOrderId: 'po-1'),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Purchase order preview'), findsOneWidget);
    expect(
      find.text('The server could not complete the request. Try again.'),
      findsOneWidget,
    );
    expect(find.text(raw), findsNothing);
  });
}
