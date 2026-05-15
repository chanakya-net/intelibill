import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/shops/presentation/widgets/shop_step_indicator.dart';

void main() {
  testWidgets('renders 3 steps and marks active step', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: ShopStepIndicator(currentStep: 2, totalSteps: 3),
        ),
      ),
    );

    final context = tester.element(find.byType(ShopStepIndicator));
    final theme = Theme.of(context);

    expect(find.byKey(const Key('shop-step-indicator-step-1')), findsOneWidget);
    expect(find.byKey(const Key('shop-step-indicator-step-2')), findsOneWidget);
    expect(find.byKey(const Key('shop-step-indicator-step-3')), findsOneWidget);

    expect(find.text('1'), findsNWidgets(1));
    expect(find.text('2'), findsNWidgets(1));
    expect(find.text('3'), findsNWidgets(1));

    final active = tester.widget<Container>(
      find.byKey(const Key('shop-step-indicator-step-2')),
    );
    final decoration = active.decoration as BoxDecoration;
    expect(decoration.color, equals(theme.colorScheme.primary));
  });
}
