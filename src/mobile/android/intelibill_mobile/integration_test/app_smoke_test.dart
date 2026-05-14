import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:intelibill_mobile/src/app/app.dart';

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('app starts and renders first screen', (tester) async {
    await tester.pumpWidget(
      const ProviderScope(
        child: IntelibillApp(),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Intelibill status'), findsOneWidget);
    expect(find.text('Mobile clean architecture sample'), findsOneWidget);
  });
}
