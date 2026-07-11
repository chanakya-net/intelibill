import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/widgets/expenses_summary_strip.dart';

Widget _buildApp({
  required int totalCount,
  required double loadedAmount,
  required int loadedActiveCount,
  required int loadedVoidedCount,
}) {
  return MaterialApp(
    theme: AppTheme.lightTheme,
    locale: const Locale('en', 'IN'),
    localizationsDelegates: AppLocalizations.localizationsDelegates,
    supportedLocales: AppLocalizations.supportedLocales,
    home: Scaffold(
      body: ExpensesSummaryStrip(
        totalCount: totalCount,
        loadedAmount: loadedAmount,
        loadedActiveCount: loadedActiveCount,
        loadedVoidedCount: loadedVoidedCount,
      ),
    ),
  );
}

void main() {
  testWidgets('shows server total count and loaded-row metrics', (
    tester,
  ) async {
    await tester.pumpWidget(
      _buildApp(
        totalCount: 42,
        loadedAmount: 1250,
        loadedActiveCount: 3,
        loadedVoidedCount: 1,
      ),
    );

    expect(find.text('Total expenses'), findsOneWidget);
    expect(find.text('42'), findsOneWidget);
    expect(find.text('Loaded amount'), findsOneWidget);
    expect(find.text(formatInr(1250)), findsOneWidget);
    expect(find.text('Loaded active'), findsOneWidget);
    expect(find.text('3'), findsOneWidget);
    expect(find.text('Loaded voided'), findsOneWidget);
    expect(find.text('1'), findsOneWidget);
  });

  testWidgets('wraps onto multiple lines on narrow layouts', (tester) async {
    tester.view.physicalSize = const Size(320, 800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      _buildApp(
        totalCount: 42,
        loadedAmount: 1250,
        loadedActiveCount: 3,
        loadedVoidedCount: 1,
      ),
    );

    expect(find.byType(Wrap), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
