import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:intelibill_mobile/src/app/app.dart';

void main() {
  group('AppRouter', () {
    testWidgets('root route renders successfully', (WidgetTester tester) async {
      await tester.pumpWidget(
        const ProviderScope(
          child: IntelibillApp(),
        ),
      );

      expect(find.byType(MaterialApp), findsOneWidget);
      expect(find.byType(IntelibillApp), findsOneWidget);
    });
  });
}
