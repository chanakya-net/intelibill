import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/items_controller.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/widgets/create_item_sheet.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_draft.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/domain/entities/purchase_order_status.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_builder_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/pages/purchase_order_builder_page.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_draft_line_card.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';

class _StubBuilderController extends PurchaseOrderBuilderController {
  _StubBuilderController(this.initialState, {this.draftToSave});

  final PurchaseOrderBuilderState initialState;
  final PurchaseOrder? draftToSave;
  final addedItems =
      <
        ({
          String itemId,
          String description,
          int expectedQuantity,
          double unitCost,
        })
      >[];
  final updatedLines =
      <
        ({
          int index,
          int expectedQuantity,
          double unitCost,
        })
      >[];
  final removedIndexes = <int>[];

  @override
  PurchaseOrderBuilderState build(String target) => initialState;

  @override
  void selectSupplier(Supplier? supplier) {
    state = state.copyWith(
      selectedSupplier: supplier,
      clearSupplier: supplier == null,
    );
  }

  @override
  void setOrderDate(DateTime? date) {
    state = state.copyWith(orderDate: date, clearOrderDate: date == null);
  }

  @override
  void setExpectedDeliveryDate(DateTime? date) {
    state = state.copyWith(
      expectedDeliveryDate: date,
      clearExpectedDeliveryDate: date == null,
    );
  }

  @override
  void setSupplierReferenceNumber(String value) {
    state = state.copyWith(supplierReferenceNumber: value);
  }

  @override
  void setNotes(String value) {
    state = state.copyWith(notes: value);
  }

  @override
  void addItem({
    required String itemId,
    required String description,
    required int expectedQuantity,
    required double unitCost,
  }) {
    addedItems.add((
      itemId: itemId,
      description: description,
      expectedQuantity: expectedQuantity,
      unitCost: unitCost,
    ));
    final line = PurchaseOrderDraftLine(
      itemId: itemId,
      description: description,
      expectedQuantity: expectedQuantity,
      unitCost: unitCost,
    );
    state = state.copyWith(lines: [...state.lines, line]);
  }

  @override
  void updateLine({
    required int index,
    required int expectedQuantity,
    required double unitCost,
  }) {
    updatedLines.add((
      index: index,
      expectedQuantity: expectedQuantity,
      unitCost: unitCost,
    ));
  }

  @override
  void removeLine(int index) {
    removedIndexes.add(index);
    final updated = [...state.lines];
    if (index >= 0 && index < updated.length) {
      updated.removeAt(index);
      state = state.copyWith(lines: updated);
    }
  }

  @override
  Future<PurchaseOrder?> save() async {
    final draft = draftToSave;
    if (draft != null) state = state.copyWith(savedDraft: draft);
    return draft;
  }
}

class _StubItemsController extends ItemsController {
  _StubItemsController(
    this._initialState, {
    this.createdItem,
    this.createFailure,
    this.refreshedItems,
  });

  final ItemsState _initialState;
  final Item? createdItem;
  final Failure? createFailure;
  final List<Item>? refreshedItems;
  int refreshCalls = 0;

  @override
  ItemsState build() => _initialState;

  @override
  void updateSearch(String query) {
    state = state.copyWith(searchQuery: query);
  }

  @override
  Future<void> refresh() async {
    refreshCalls++;
    state = state.copyWith(items: refreshedItems ?? [?createdItem]);
  }

  @override
  Future<Item?> createItem({
    required String name,
    required String barcode,
    required String uom,
    String? description,
  }) async {
    final failure = createFailure;
    if (failure != null) {
      state = state.copyWith(submitFailure: failure);
      return null;
    }
    final created = createdItem;
    if (created != null) await refresh();
    return created;
  }
}

void main() {
  Widget buildApp(
    PurchaseOrderBuilderState state, {
    String target = 'new',
    PurchaseOrder? draftToSave,
    GoRouter? router,
    ItemsState itemsState = const ItemsState(),
    Item? createdItem,
    Failure? createFailure,
    List<Item>? refreshedItems,
    void Function(_StubBuilderController controller)? onControllerCreated,
    void Function(_StubItemsController controller)? onItemsControllerCreated,
  }) {
    return ProviderScope(
      overrides: [
        purchaseOrderBuilderControllerProvider(target).overrideWith(
          () {
            final controller = _StubBuilderController(
              state,
              draftToSave: draftToSave,
            );
            onControllerCreated?.call(controller);
            return controller;
          },
        ),
        itemsControllerProvider.overrideWith(
          () {
            final controller = _StubItemsController(
              itemsState,
              createdItem: createdItem,
              createFailure: createFailure,
              refreshedItems: refreshedItems,
            );
            onItemsControllerCreated?.call(controller);
            return controller;
          },
        ),
      ],
      child: router == null
          ? MaterialApp(
              theme: AppTheme.lightTheme,
              localizationsDelegates: AppLocalizations.localizationsDelegates,
              supportedLocales: AppLocalizations.supportedLocales,
              home: PurchaseOrderBuilderPage(target: target),
            )
          : MaterialApp.router(
              theme: AppTheme.lightTheme,
              localizationsDelegates: AppLocalizations.localizationsDelegates,
              supportedLocales: AppLocalizations.supportedLocales,
              routerConfig: router,
            ),
    );
  }

  testWidgets('renders keyboard-safe header fields and sticky save action', (
    tester,
  ) async {
    await tester.pumpWidget(
      buildApp(
        PurchaseOrderBuilderState(
          suppliers: [_supplier()],
        ),
      ),
    );

    expect(find.byKey(PurchaseOrderBuilderPage.pageKey), findsOneWidget);
    expect(
      find.byKey(PurchaseOrderBuilderPage.supplierFieldKey),
      findsOneWidget,
    );
    expect(
      find.byKey(PurchaseOrderBuilderPage.orderDateFieldKey),
      findsOneWidget,
    );
    expect(
      find.byKey(PurchaseOrderBuilderPage.expectedDeliveryDateFieldKey),
      findsOneWidget,
    );
    expect(
      find.byKey(PurchaseOrderBuilderPage.referenceFieldKey),
      findsOneWidget,
    );
    expect(find.byKey(PurchaseOrderBuilderPage.notesFieldKey), findsOneWidget);
    expect(find.byKey(PurchaseOrderBuilderPage.saveButtonKey), findsOneWidget);
    expect(find.byType(SingleChildScrollView), findsOneWidget);
  });

  testWidgets('prefills an edit builder from its loaded draft state', (
    tester,
  ) async {
    const line = PurchaseOrderDraftLine(
      itemId: 'item-1',
      description: 'Widget',
      expectedQuantity: 2,
      unitCost: 4.5,
    );
    await tester.pumpWidget(
      buildApp(
        PurchaseOrderBuilderState(
          suppliers: [_supplier()],
          selectedSupplier: _supplier(),
          supplierReferenceNumber: 'REF-OLD',
          notes: 'Original note',
          lines: const [line],
        ),
        target: 'po-1',
      ),
    );

    expect(
      tester
          .widget<TextFormField>(
            find.byKey(PurchaseOrderBuilderPage.referenceFieldKey),
          )
          .controller!
          .text,
      'REF-OLD',
    );
    expect(
      tester
          .widget<TextFormField>(
            find.byKey(PurchaseOrderBuilderPage.notesFieldKey),
          )
          .controller!
          .text,
      'Original note',
    );
    expect(find.text('Line total: 9.00'), findsOneWidget);
  });

  testWidgets('shows only the supplied active supplier option', (tester) async {
    await tester.pumpWidget(
      buildApp(PurchaseOrderBuilderState(suppliers: [_supplier()])),
    );

    await tester.tap(find.byKey(PurchaseOrderBuilderPage.supplierFieldKey));
    await tester.pumpAndSettle();

    expect(find.text('Fresh Supplier'), findsOneWidget);
  });

  testWidgets('preserves form and shows error message on save failure', (
    tester,
  ) async {
    const failure = Failure.server(statusCode: 500);
    await tester.pumpWidget(
      buildApp(
        PurchaseOrderBuilderState(
          suppliers: [_supplier()],
          failure: failure,
        ),
      ),
    );

    expect(find.byKey(PurchaseOrderBuilderPage.pageKey), findsOneWidget);
    expect(
      find.byKey(PurchaseOrderBuilderPage.supplierFieldKey),
      findsOneWidget,
    );
    expect(
      find.text('The server could not complete the request. Try again.'),
      findsOneWidget,
    );
  });

  testWidgets('renders formatted date selected through date picker', (
    tester,
  ) async {
    await tester.pumpWidget(
      buildApp(
        PurchaseOrderBuilderState(
          suppliers: [_supplier()],
        ),
      ),
    );

    final field = find.byKey(PurchaseOrderBuilderPage.orderDateFieldKey);
    final selectedDate = DateTime(
      DateTime.now().year,
      DateTime.now().month,
      15,
    );
    final formattedDate = MaterialLocalizations.of(
      tester.element(field),
    ).formatMediumDate(selectedDate);

    await tester.tap(field);
    await tester.pumpAndSettle();
    await tester.tap(find.text('15').last);
    await tester.tap(find.text('OK'));
    await tester.pumpAndSettle();

    expect(
      tester
          .widget<TextFormField>(
            find.descendant(of: field, matching: find.byType(TextFormField)),
          )
          .controller!
          .text,
      formattedDate,
    );
  });

  testWidgets(
    'navigates to saved purchase order detail after successful save',
    (
      tester,
    ) async {
      final router = GoRouter(
        initialLocation: AppRoutes.purchaseOrderNew,
        routes: [
          GoRoute(
            path: AppRoutes.purchaseOrderNew,
            builder: (_, _) => const PurchaseOrderBuilderPage(target: 'new'),
          ),
          GoRoute(
            path: AppRoutes.purchaseOrderDetail,
            builder: (_, state) =>
                Text(state.pathParameters['purchaseOrderId']!),
          ),
        ],
      );
      await tester.pumpWidget(
        buildApp(
          PurchaseOrderBuilderState(suppliers: [_supplier()]),
          draftToSave: _savedPurchaseOrder(),
          router: router,
        ),
      );

      await tester.tap(find.byKey(PurchaseOrderBuilderPage.saveButtonKey));
      await tester.pumpAndSettle();

      expect(
        router.routerDelegate.currentConfiguration.uri.path,
        AppRoutes.purchaseOrderDetailFor('po-1'),
      );
      expect(find.text('po-1'), findsOneWidget);
    },
  );

  testWidgets('opens the edit builder from an edit deep link', (tester) async {
    final router = GoRouter(
      initialLocation: AppRoutes.purchaseOrderEditFor('po-1'),
      routes: [
        GoRoute(
          path: AppRoutes.purchaseOrderEdit,
          builder: (_, state) => PurchaseOrderBuilderPage(
            target: state.pathParameters['purchaseOrderId']!,
          ),
        ),
      ],
    );
    await tester.pumpWidget(
      buildApp(
        PurchaseOrderBuilderState(suppliers: [_supplier()]),
        target: 'po-1',
        router: router,
      ),
    );
    addTearDown(router.dispose);

    expect(find.byKey(PurchaseOrderBuilderPage.pageKey), findsOneWidget);
    expect(
      router.routerDelegate.currentConfiguration.uri.path,
      AppRoutes.purchaseOrderEditFor('po-1'),
    );
  });

  testWidgets('adds a selected item from the dialog', (tester) async {
    _StubBuilderController? controller;
    await tester.pumpWidget(
      buildApp(
        PurchaseOrderBuilderState(suppliers: [_supplier()]),
        itemsState: ItemsState(items: [_item('item-1', 'Widget')]),
        onControllerCreated: (value) => controller = value,
      ),
    );

    final addItemButton = find.byKey(PurchaseOrderBuilderPage.addItemButtonKey);
    await tester.ensureVisible(addItemButton);
    await tester.tap(addItemButton);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(PurchaseOrderBuilderPage.addItemFieldKey));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Widget'));
    await tester.pump();

    expect(
      tester
          .widget<FilledButton>(
            find.byKey(PurchaseOrderBuilderPage.addItemConfirmButtonKey),
          )
          .onPressed,
      isNotNull,
    );
    await tester.tap(
      find.byKey(PurchaseOrderBuilderPage.addItemConfirmButtonKey),
    );
    await tester.pumpAndSettle();

    expect(controller!.addedItems, hasLength(1));
    expect(controller!.addedItems.single.itemId, 'item-1');
    expect(controller!.addedItems.single.description, 'Widget');
  });

  testWidgets('renders and edits, totals, and removes draft lines', (
    tester,
  ) async {
    _StubBuilderController? controller;
    const line = PurchaseOrderDraftLine(
      itemId: 'item-1',
      description: 'Widget',
      expectedQuantity: 2,
      unitCost: 4.5,
    );
    await tester.pumpWidget(
      buildApp(
        PurchaseOrderBuilderState(
          suppliers: [_supplier()],
          lines: const [line],
        ),
        onControllerCreated: (value) => controller = value,
      ),
    );

    expect(find.byKey(PurchaseOrderBuilderPage.linesHeaderKey), findsOneWidget);
    expect(find.text('Line total: 9.00'), findsOneWidget);
    expect(
      find.byKey(PurchaseOrderBuilderPage.expectedTotalKey),
      findsOneWidget,
    );
    expect(find.text('9.00'), findsOneWidget);

    final card = find.byType(PurchaseOrderDraftLineCard);
    final fields = find.descendant(
      of: card,
      matching: find.byType(TextFormField),
    );
    await tester.enterText(fields.at(0), '');
    await tester.enterText(fields.at(0), '3');
    expect(controller!.updatedLines, isEmpty);
    expect(find.text('Invalid line values'), findsNothing);
    await tester.testTextInput.receiveAction(TextInputAction.done);
    await tester.pump();
    expect(controller!.updatedLines, hasLength(1));
    expect(controller!.updatedLines.single.expectedQuantity, 3);

    await tester.enterText(fields.at(1), '6.25');
    await tester.testTextInput.receiveAction(TextInputAction.done);
    await tester.pump();
    expect(controller!.updatedLines, hasLength(2));
    expect(controller!.updatedLines.last.unitCost, 6.25);

    final removeButton = find.text('Remove');
    await tester.ensureVisible(removeButton);
    await tester.tap(removeButton);
    await tester.pump();
    expect(controller!.removedIndexes, [0]);
  });

  testWidgets('filters items in the add item dialog', (tester) async {
    await tester.pumpWidget(
      buildApp(
        PurchaseOrderBuilderState(suppliers: [_supplier()]),
        itemsState: ItemsState(
          items: [_item('item-1', 'Widget'), _item('item-2', 'Gadget')],
        ),
      ),
    );

    final addItemButton = find.byKey(PurchaseOrderBuilderPage.addItemButtonKey);
    await tester.ensureVisible(addItemButton);
    await tester.tap(addItemButton);
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(PurchaseOrderBuilderPage.itemSearchKey),
      'Wid',
    );
    await tester.pump();
    await tester.tap(find.byKey(PurchaseOrderBuilderPage.addItemFieldKey));
    await tester.pumpAndSettle();

    expect(find.text('Widget'), findsOneWidget);
    expect(find.text('Gadget'), findsNothing);
  });

  testWidgets('quick creates a missing item and preserves builder draft', (
    tester,
  ) async {
    _StubBuilderController? builderController;
    _StubItemsController? itemsController;
    final state = PurchaseOrderBuilderState(
      suppliers: [_supplier()],
      selectedSupplier: _supplier(),
      supplierReferenceNumber: 'REF-1',
      notes: 'Keep this note',
      lines: const [
        PurchaseOrderDraftLine(
          itemId: 'existing-item',
          description: 'Existing item',
          expectedQuantity: 2,
          unitCost: 3,
        ),
      ],
    );
    final created = _item('created-item', 'Missing Widget');
    await tester.pumpWidget(
      buildApp(
        state,
        createdItem: created,
        onControllerCreated: (value) => builderController = value,
        onItemsControllerCreated: (value) => itemsController = value,
      ),
    );

    final addItemButton = find.byKey(PurchaseOrderBuilderPage.addItemButtonKey);
    await tester.ensureVisible(addItemButton);
    await tester.tap(addItemButton);
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(PurchaseOrderBuilderPage.itemSearchKey),
      'Missing Widget',
    );
    await tester.pump();
    final quickCreateButton = find.byKey(
      PurchaseOrderBuilderPage.quickCreateItemButtonKey,
    );
    await tester.ensureVisible(quickCreateButton);
    await tester.tap(quickCreateButton);
    await tester.pumpAndSettle();

    expect(
      tester
          .widget<TextFormField>(find.byKey(CreateItemSheet.nameFieldKey))
          .controller!
          .text,
      'Missing Widget',
    );
    await tester.enterText(
      find.byKey(CreateItemSheet.barcodeFieldKey),
      'CREATED-1',
    );
    await tester.enterText(find.byKey(CreateItemSheet.uomFieldKey), 'pcs');
    await tester.tap(find.byKey(CreateItemSheet.submitButtonKey));
    await tester.pumpAndSettle();

    expect(itemsController!.refreshCalls, 1);
    expect(builderController!.state.selectedCatalogItem, created);
    expect(builderController!.state.selectedSupplier, _supplier());
    expect(builderController!.state.supplierReferenceNumber, 'REF-1');
    expect(builderController!.state.notes, 'Keep this note');
    expect(builderController!.state.lines, state.lines);
    expect(
      tester
          .widget<FilledButton>(
            find.byKey(PurchaseOrderBuilderPage.addItemConfirmButtonKey),
          )
          .onPressed,
      isNotNull,
    );
  });

  testWidgets('selects a renamed quick-created item without dropdown errors', (
    tester,
  ) async {
    final created = _item('created-item', 'Bar');
    _StubItemsController? itemsController;
    _StubBuilderController? builderController;
    await tester.pumpWidget(
      buildApp(
        PurchaseOrderBuilderState(suppliers: [_supplier()]),
        createdItem: created,
        refreshedItems: [created, _item('foo-item', 'Foo match')],
        onControllerCreated: (controller) => builderController = controller,
        onItemsControllerCreated: (controller) => itemsController = controller,
      ),
    );

    final addItemButton = find.byKey(PurchaseOrderBuilderPage.addItemButtonKey);
    await tester.ensureVisible(addItemButton);
    await tester.tap(addItemButton);
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(PurchaseOrderBuilderPage.itemSearchKey),
      'Foo',
    );
    await tester.pump();
    await tester.tap(
      find.byKey(PurchaseOrderBuilderPage.quickCreateItemButtonKey),
    );
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(CreateItemSheet.nameFieldKey), 'Bar');
    await tester.enterText(
      find.byKey(CreateItemSheet.barcodeFieldKey),
      'BAR-1',
    );
    await tester.enterText(find.byKey(CreateItemSheet.uomFieldKey), 'pcs');
    await tester.tap(find.byKey(CreateItemSheet.submitButtonKey));
    await tester.pumpAndSettle();

    expect(tester.takeException(), isNull);
    expect(itemsController!.refreshCalls, 1);
    expect(itemsController!.state.searchQuery, 'Bar');
    expect(builderController!.state.selectedCatalogItem, created);
    expect(
      tester
          .widget<FilledButton>(
            find.byKey(PurchaseOrderBuilderPage.addItemConfirmButtonKey),
          )
          .onPressed,
      isNotNull,
    );
  });

  testWidgets('cancelling or failing quick create leaves draft unchanged', (
    tester,
  ) async {
    _StubBuilderController? builderController;
    final state = PurchaseOrderBuilderState(
      suppliers: [_supplier()],
      supplierReferenceNumber: 'REF-2',
      notes: 'Do not lose me',
      lines: const [
        PurchaseOrderDraftLine(
          itemId: 'existing-item',
          description: 'Existing item',
          expectedQuantity: 1,
          unitCost: 4,
        ),
      ],
    );
    await tester.pumpWidget(
      buildApp(
        state,
        createFailure: const Failure.network(),
        onControllerCreated: (value) => builderController = value,
      ),
    );

    final addItemButton = find.byKey(PurchaseOrderBuilderPage.addItemButtonKey);
    await tester.ensureVisible(addItemButton);
    await tester.tap(addItemButton);
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(PurchaseOrderBuilderPage.itemSearchKey),
      'Unavailable',
    );
    await tester.pump();
    final quickCreateButton = find.byKey(
      PurchaseOrderBuilderPage.quickCreateItemButtonKey,
    );
    await tester.ensureVisible(quickCreateButton);
    await tester.tap(quickCreateButton);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(CreateItemSheet.cancelButtonKey));
    await tester.pumpAndSettle();

    expect(builderController!.state.selectedCatalogItem, isNull);
    expect(builderController!.state.supplierReferenceNumber, 'REF-2');
    expect(builderController!.state.notes, 'Do not lose me');
    expect(builderController!.state.lines, state.lines);

    await tester.ensureVisible(quickCreateButton);
    await tester.tap(quickCreateButton);
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(CreateItemSheet.barcodeFieldKey),
      'FAIL-1',
    );
    await tester.enterText(find.byKey(CreateItemSheet.uomFieldKey), 'pcs');
    await tester.tap(find.byKey(CreateItemSheet.submitButtonKey));
    await tester.pump();

    expect(find.byKey(CreateItemSheet.nameFieldKey), findsOneWidget);
    expect(builderController!.state.selectedCatalogItem, isNull);
    expect(builderController!.state.supplierReferenceNumber, 'REF-2');
    expect(builderController!.state.notes, 'Do not lose me');
    expect(builderController!.state.lines, state.lines);
  });
  testWidgets('offers Place only for an eligible persisted Draft', (
    tester,
  ) async {
    const line = PurchaseOrderDraftLine(
      itemId: 'item-1',
      description: 'Widget',
      expectedQuantity: 1,
      unitCost: 10,
    );
    await tester.pumpWidget(
      buildApp(
        PurchaseOrderBuilderState(
          suppliers: [_supplier()],
          selectedSupplier: _supplier(),
          isEditableDraft: true,
          lines: const [line],
        ),
        target: 'po-1',
      ),
    );
    expect(
      find.byKey(PurchaseOrderBuilderPage.placeButtonKey),
      findsOneWidget,
    );

    await tester.pumpWidget(const SizedBox());
    await tester.pump();
    await tester.pumpWidget(
      buildApp(
        PurchaseOrderBuilderState(
          suppliers: [_supplier()],
          selectedSupplier: _supplier(),
          isEditableDraft: true,
        ),
        target: 'po-2',
      ),
    );
    expect(find.byKey(PurchaseOrderBuilderPage.placeButtonKey), findsNothing);
  });
}

PurchaseOrder _savedPurchaseOrder() => PurchaseOrder(
  purchaseOrderId: 'po-1',
  purchaseOrderNumber: 'PO-2026-000001',
  status: PurchaseOrderStatus.draft,
  lines: const [],
  expectedTotal: 0,
  createdAt: DateTime(2026, 7, 19),
);

Supplier _supplier() => const Supplier(
  supplierId: 'supplier-1',
  name: 'Fresh Supplier',
  isSystem: false,
  isActive: true,
  isPreferred: true,
  balanceDue: 0,
);

Item _item(String itemId, String name) => Item(
  itemId: itemId,
  name: name,
  barcode: '$itemId-barcode',
  uom: 'pcs',
  isActive: true,
  currentStock: 0,
);
