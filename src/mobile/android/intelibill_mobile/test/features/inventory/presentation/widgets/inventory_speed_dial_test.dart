import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/widgets/inventory_speed_dial.dart';

void main() {
  group('InventorySpeedDial', () {
    testWidgets('expands labeled circular actions when main button is tapped', (
      tester,
    ) async {
      var addProductTapped = false;

      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            floatingActionButtonLocation: FloatingActionButtonLocation.endFloat,
            floatingActionButton: InventorySpeedDial(
              mainFabKey: const Key('speed-dial-main'),
              actions: [
                InventorySpeedDialAction(
                  key: const Key('speed-dial-add-product'),
                  label: 'Add Product',
                  icon: Icons.add_box_outlined,
                  onTap: () => addProductTapped = true,
                ),
                InventorySpeedDialAction(
                  label: 'Add Inventory',
                  icon: Icons.inventory_outlined,
                  onTap: () {},
                ),
                InventorySpeedDialAction(
                  label: 'Batch Overview',
                  icon: Icons.layers_outlined,
                  onTap: () {},
                ),
                InventorySpeedDialAction(
                  label: 'Adjustment History',
                  icon: Icons.history,
                  onTap: () {},
                ),
              ],
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Add Product'), findsNothing);
      expect(find.byIcon(Icons.add), findsOneWidget);

      await tester.tap(find.byKey(const Key('speed-dial-main')));
      await tester.pumpAndSettle();

      expect(find.text('Add Product'), findsOneWidget);
      expect(find.text('Add Inventory'), findsOneWidget);
      expect(find.text('Batch Overview'), findsOneWidget);
      expect(find.text('Adjustment History'), findsOneWidget);
      expect(find.byIcon(Icons.close), findsOneWidget);

      await tester.tap(find.byKey(const Key('speed-dial-add-product')));
      await tester.pumpAndSettle();

      expect(addProductTapped, isTrue);
      expect(find.text('Add Product'), findsNothing);
      expect(find.byIcon(Icons.add), findsOneWidget);
    });
  });
}
