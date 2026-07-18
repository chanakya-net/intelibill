import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_close_sheet.dart';

void main() {
  testWidgets('accepts trimmed reasons at both boundaries', (tester) async {
    final reasons = <String>[];
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: PurchaseOrderCloseSheet(
            onClose: (reason) async => reasons.add(reason),
          ),
        ),
      ),
    );

    await tester.enterText(find.byType(TextField), ' A ');
    await tester.pump();
    expect(
      tester.widget<ElevatedButton>(find.byType(ElevatedButton)).onPressed,
      isNotNull,
    );
    await tester.tap(find.byType(ElevatedButton));
    await tester.pumpAndSettle();
    expect(reasons, ['A']);
  });

  testWidgets('accepts a 500-character reason', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: PurchaseOrderCloseSheet(onClose: (_) async {}),
        ),
      ),
    );
    await tester.enterText(find.byType(TextField), 'x' * 500);
    await tester.pump();
    expect(
      tester.widget<ElevatedButton>(find.byType(ElevatedButton)).onPressed,
      isNotNull,
    );
  });

  testWidgets('accepts whitespace-padded 500-character trimmed reason', (
    tester,
  ) async {
    final reasons = <String>[];
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: PurchaseOrderCloseSheet(
            onClose: (reason) async => reasons.add(reason),
          ),
        ),
      ),
    );
    final paddedReason = ' ${'x' * 500} ';
    await tester.enterText(find.byType(TextField), paddedReason);
    await tester.pump();
    expect(
      tester.widget<ElevatedButton>(find.byType(ElevatedButton)).onPressed,
      isNotNull,
    );
    await tester.tap(find.byType(ElevatedButton));
    await tester.pumpAndSettle();
    expect(reasons, ['x' * 500]);
  });

  testWidgets('disables close while the callback is pending', (tester) async {
    final completer = Completer<void>();
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: PurchaseOrderCloseSheet(onClose: (_) => completer.future),
        ),
      ),
    );
    await tester.enterText(find.byType(TextField), 'reason');
    await tester.pump();
    await tester.tap(find.byType(ElevatedButton));
    await tester.pump();

    expect(
      tester.widget<ElevatedButton>(find.byType(ElevatedButton)).onPressed,
      isNull,
    );
    completer.complete();
    await tester.pumpAndSettle();
  });
}
