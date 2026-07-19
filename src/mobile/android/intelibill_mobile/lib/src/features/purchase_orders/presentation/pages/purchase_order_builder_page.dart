import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/items_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_builder_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_draft_line_card.dart';
import 'package:intelibill_mobile/src/features/suppliers/domain/entities/supplier.dart';

class PurchaseOrderBuilderPage extends ConsumerStatefulWidget {
  const PurchaseOrderBuilderPage({required this.target, super.key});

  static const pageKey = Key('purchase-order-builder-page');
  static const supplierFieldKey = Key('purchase-order-builder-supplier');
  static const orderDateFieldKey = Key('purchase-order-builder-order-date');
  static const expectedDeliveryDateFieldKey = Key(
    'purchase-order-builder-expected-delivery-date',
  );
  static const referenceFieldKey = Key('purchase-order-builder-reference');
  static const notesFieldKey = Key('purchase-order-builder-notes');
  static const saveButtonKey = Key('purchase-order-builder-save');
  static const addItemFieldKey = Key('purchase-order-builder-add-item');
  static const itemSearchKey = Key('purchase-order-builder-item-search');
  static const addItemButtonKey = Key('purchase-order-builder-add-item-btn');
  static const addItemConfirmButtonKey = Key(
    'purchase-order-builder-add-item-confirm-btn',
  );
  static const linesHeaderKey = Key('purchase-order-builder-lines-header');
  static const expectedTotalKey = Key('purchase-order-builder-expected-total');

  final String target;

  @override
  ConsumerState<PurchaseOrderBuilderPage> createState() =>
      _PurchaseOrderBuilderPageState();
}

class _PurchaseOrderBuilderPageState
    extends ConsumerState<PurchaseOrderBuilderPage> {
  final _referenceController = TextEditingController();
  final _notesController = TextEditingController();
  final _orderDateController = TextEditingController();
  final _expectedDeliveryDateController = TextEditingController();

  @override
  void dispose() {
    _referenceController.dispose();
    _notesController.dispose();
    _orderDateController.dispose();
    _expectedDeliveryDateController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final provider = purchaseOrderBuilderControllerProvider(widget.target);
    ref.listen(provider, (previous, next) {
      final draft = next.savedDraft;
      if (draft != null &&
          previous?.savedDraft?.purchaseOrderId != draft.purchaseOrderId) {
        context.go(AppRoutes.purchaseOrderDetailFor(draft.purchaseOrderId));
      }
    });
    final state = ref.watch(provider);

    return Scaffold(
      key: PurchaseOrderBuilderPage.pageKey,
      appBar: AppBar(title: Text(l10n.purchaseOrderBuilderTitle)),
      body: SafeArea(
        child: _BuilderBody(
          state: state,
          referenceController: _referenceController,
          notesController: _notesController,
          orderDateController: _orderDateController,
          expectedDeliveryDateController: _expectedDeliveryDateController,
          onSupplierChanged: (supplier) =>
              ref.read(provider.notifier).selectSupplier(supplier),
          onOrderDateChanged: (date) =>
              ref.read(provider.notifier).setOrderDate(date),
          onExpectedDeliveryDateChanged: (date) =>
              ref.read(provider.notifier).setExpectedDeliveryDate(date),
          onReferenceChanged: (value) =>
              ref.read(provider.notifier).setSupplierReferenceNumber(value),
          onNotesChanged: (value) =>
              ref.read(provider.notifier).setNotes(value),
          onAddItem: () => _showAddItemDialog(context, ref, provider, l10n),
          onUpdateLine:
              ({
                required index,
                required expectedQuantity,
                required unitCost,
              }) => ref
                  .read(provider.notifier)
                  .updateLine(
                    index: index,
                    expectedQuantity: expectedQuantity,
                    unitCost: unitCost,
                  ),
          onRemoveLine: (index) =>
              ref.read(provider.notifier).removeLine(index),
          onRetry: () => ref.read(provider.notifier).loadSuppliers(),
          l10n: l10n,
        ),
      ),
      bottomNavigationBar: SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 12),
          child: FilledButton(
            key: PurchaseOrderBuilderPage.saveButtonKey,
            onPressed: state.isSaving
                ? null
                : () => ref.read(provider.notifier).save(),
            child: state.isSaving
                ? const SizedBox(
                    height: 20,
                    width: 20,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : Text(l10n.purchaseOrderBuilderSave),
          ),
        ),
      ),
    );
  }

  Future<void> _showAddItemDialog(
    BuildContext context,
    WidgetRef ref,
    dynamic provider,
    AppLocalizations l10n,
  ) async {
    Item? selectedItem;
    int quantity = 1;
    double unitCost = 0.0;

    await showDialog<void>(
      context: context,
      builder: (context) => Consumer(
        builder: (context, ref, _) {
          final itemsState = ref.watch(itemsControllerProvider);
          return StatefulBuilder(
            builder: (context, setState) => AlertDialog(
              title: Text(l10n.purchaseOrderBuilderAddItemTitle),
              content: SingleChildScrollView(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    TextField(
                      key: PurchaseOrderBuilderPage.itemSearchKey,
                      decoration: InputDecoration(
                        labelText: l10n.commonSearch,
                      ),
                      onChanged: (query) => ref
                          .read(itemsControllerProvider.notifier)
                          .updateSearch(query),
                    ),
                    const SizedBox(height: 16),
                    DropdownButtonFormField<Item>(
                      key: PurchaseOrderBuilderPage.addItemFieldKey,
                      value: selectedItem,
                      isExpanded: true,
                      decoration: InputDecoration(
                        labelText: l10n.purchaseOrderBuilderAddItemLabel,
                      ),
                      items: itemsState.filteredItems
                          .map(
                            (item) => DropdownMenuItem(
                              value: item,
                              child: Text(item.name),
                            ),
                          )
                          .toList(),
                      onChanged: (item) => setState(() => selectedItem = item),
                    ),
                    const SizedBox(height: 16),
                    TextFormField(
                      initialValue: quantity.toString(),
                      keyboardType: TextInputType.number,
                      decoration: InputDecoration(
                        labelText: l10n.purchaseOrderBuilderQuantityLabel,
                      ),
                      onChanged: (value) =>
                          setState(() => quantity = int.tryParse(value) ?? 1),
                    ),
                    const SizedBox(height: 16),
                    TextFormField(
                      initialValue: unitCost.toStringAsFixed(2),
                      keyboardType: const TextInputType.numberWithOptions(
                        decimal: true,
                      ),
                      decoration: InputDecoration(
                        labelText: l10n.purchaseOrderBuilderUnitCostLabel,
                      ),
                      onChanged: (value) => setState(
                        () => unitCost = double.tryParse(value) ?? 0.0,
                      ),
                    ),
                  ],
                ),
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.pop(context),
                  child: Text(l10n.commonCancel),
                ),
                FilledButton(
                  key: PurchaseOrderBuilderPage.addItemConfirmButtonKey,
                  onPressed: selectedItem == null
                      ? null
                      : () {
                          ref
                              .read(provider.notifier)
                              .addItem(
                                itemId: selectedItem!.itemId,
                                description: selectedItem!.name,
                                expectedQuantity: quantity,
                                unitCost: unitCost,
                              );
                          Navigator.pop(context);
                        },
                  child: Text(l10n.commonSave),
                ),
              ],
            ),
          );
        },
      ),
    );
  }
}

class _BuilderBody extends StatelessWidget {
  const _BuilderBody({
    required this.state,
    required this.referenceController,
    required this.notesController,
    required this.orderDateController,
    required this.expectedDeliveryDateController,
    required this.onSupplierChanged,
    required this.onOrderDateChanged,
    required this.onExpectedDeliveryDateChanged,
    required this.onReferenceChanged,
    required this.onNotesChanged,
    required this.onAddItem,
    required this.onUpdateLine,
    required this.onRemoveLine,
    required this.onRetry,
    required this.l10n,
  });

  final PurchaseOrderBuilderState state;
  final TextEditingController referenceController;
  final TextEditingController notesController;
  final TextEditingController orderDateController;
  final TextEditingController expectedDeliveryDateController;
  final ValueChanged<Supplier?> onSupplierChanged;
  final ValueChanged<DateTime?> onOrderDateChanged;
  final ValueChanged<DateTime?> onExpectedDeliveryDateChanged;
  final ValueChanged<String> onReferenceChanged;
  final ValueChanged<String> onNotesChanged;
  final VoidCallback onAddItem;
  final Function({
    required int index,
    required int expectedQuantity,
    required double unitCost,
  })
  onUpdateLine;
  final Function(int) onRemoveLine;
  final VoidCallback onRetry;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    if (state.isLoadingSuppliers && state.suppliers.isEmpty) {
      return const Center(child: CircularProgressIndicator());
    }
    if (state.isSupplierLoadFailure && state.suppliers.isEmpty) {
      return _ErrorView(failure: state.failure!, onRetry: onRetry, l10n: l10n);
    }

    return Form(
      child: SingleChildScrollView(
        padding: EdgeInsets.fromLTRB(
          16,
          16,
          16,
          24 + MediaQuery.of(context).viewInsets.bottom,
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            DropdownButtonFormField<Supplier>(
              key: PurchaseOrderBuilderPage.supplierFieldKey,
              initialValue: state.selectedSupplier,
              isExpanded: true,
              decoration: InputDecoration(
                labelText: l10n.purchaseOrderBuilderSupplier,
              ),
              hint: Text(l10n.purchaseOrderBuilderNoSupplier),
              items: [
                DropdownMenuItem<Supplier>(
                  child: Text(l10n.purchaseOrderBuilderNoSupplier),
                ),
                ...state.suppliers.map(
                  (supplier) => DropdownMenuItem<Supplier>(
                    value: supplier,
                    child: Text(supplier.name),
                  ),
                ),
              ],
              onChanged: onSupplierChanged,
            ),
            const SizedBox(height: 16),
            _DateField(
              key: PurchaseOrderBuilderPage.orderDateFieldKey,
              label: l10n.purchaseOrderBuilderOrderDate,
              value: state.orderDate,
              controller: orderDateController,
              onChanged: onOrderDateChanged,
              l10n: l10n,
            ),
            const SizedBox(height: 16),
            _DateField(
              key: PurchaseOrderBuilderPage.expectedDeliveryDateFieldKey,
              label: l10n.purchaseOrderBuilderExpectedDeliveryDate,
              value: state.expectedDeliveryDate,
              controller: expectedDeliveryDateController,
              onChanged: onExpectedDeliveryDateChanged,
              l10n: l10n,
            ),
            const SizedBox(height: 16),
            TextFormField(
              key: PurchaseOrderBuilderPage.referenceFieldKey,
              controller: referenceController,
              maxLength:
                  PurchaseOrderBuilderController.supplierReferenceMaxLength,
              textInputAction: TextInputAction.next,
              decoration: InputDecoration(
                labelText: l10n.purchaseOrderBuilderReference,
              ),
              onChanged: onReferenceChanged,
            ),
            const SizedBox(height: 8),
            TextFormField(
              key: PurchaseOrderBuilderPage.notesFieldKey,
              controller: notesController,
              maxLength: PurchaseOrderBuilderController.notesMaxLength,
              maxLines: 4,
              textInputAction: TextInputAction.newline,
              decoration: InputDecoration(
                labelText: l10n.purchaseOrderBuilderNotes,
              ),
              onChanged: onNotesChanged,
            ),
            if (state.failure != null) ...[
              const SizedBox(height: 8),
              Text(
                state.failure!.message ?? l10n.purchaseOrderBuilderSaveError,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            ],
            if (state.suppliers.isEmpty)
              Padding(
                padding: const EdgeInsets.only(top: 16),
                child: Text(l10n.purchaseOrderBuilderNoSuppliers),
              ),
            if (state.lines.isNotEmpty) ...[
              const SizedBox(height: 24),
              Divider(),
              const SizedBox(height: 16),
              Text(
                l10n.purchaseOrderBuilderLinesHeader,
                key: PurchaseOrderBuilderPage.linesHeaderKey,
                style: Theme.of(context).textTheme.titleLarge,
              ),
              const SizedBox(height: 12),
              ...state.lines.asMap().entries.map((entry) {
                final index = entry.key;
                final line = entry.value;
                return Padding(
                  padding: const EdgeInsets.only(bottom: 12),
                  child: PurchaseOrderDraftLineCard(
                    line: line,
                    onUpdate:
                        ({required expectedQuantity, required unitCost}) =>
                            onUpdateLine(
                              index: index,
                              expectedQuantity: expectedQuantity,
                              unitCost: unitCost,
                            ),
                    onRemove: () => onRemoveLine(index),
                  ),
                );
              }),
              const SizedBox(height: 16),
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Text(
                    l10n.purchaseOrderBuilderExpectedTotal,
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  Text(
                    key: PurchaseOrderBuilderPage.expectedTotalKey,
                    state.expectedTotal.toStringAsFixed(2),
                    style: Theme.of(context).textTheme.titleLarge?.copyWith(
                      fontWeight: FontWeight.bold,
                    ),
                  ),
                ],
              ),
            ],
            const SizedBox(height: 16),
            FilledButton.tonal(
              key: PurchaseOrderBuilderPage.addItemButtonKey,
              onPressed: onAddItem,
              child: Text(l10n.purchaseOrderBuilderAddItemTitle),
            ),
          ],
        ),
      ),
    );
  }
}

class _DateField extends StatefulWidget {
  const _DateField({
    super.key,
    required this.label,
    required this.value,
    required this.controller,
    required this.onChanged,
    required this.l10n,
  });

  final String label;
  final DateTime? value;
  final TextEditingController controller;
  final ValueChanged<DateTime?> onChanged;
  final AppLocalizations l10n;

  @override
  State<_DateField> createState() => _DateFieldState();
}

class _DateFieldState extends State<_DateField> {
  @override
  void initState() {
    super.initState();
    _scheduleControllerSync();
  }

  @override
  void didUpdateWidget(_DateField oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.value != oldWidget.value) {
      _scheduleControllerSync();
    }
  }

  void _scheduleControllerSync() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _syncController();
    });
  }

  void _syncController() {
    final text = widget.value == null
        ? ''
        : MaterialLocalizations.of(context).formatMediumDate(widget.value!);
    if (widget.controller.text != text) widget.controller.text = text;
  }

  @override
  Widget build(BuildContext context) {
    return TextFormField(
      readOnly: true,
      controller: widget.controller,
      decoration: InputDecoration(
        labelText: widget.label,
        hintText: widget.l10n.purchaseOrderBuilderSelectDate,
        suffixIcon: const Icon(Icons.calendar_today),
      ),
      onTap: () async {
        final date = await showDatePicker(
          context: context,
          initialDate: widget.value ?? DateTime.now(),
          firstDate: DateTime(2000),
          lastDate: DateTime(2100),
        );
        if (date != null) widget.onChanged(date);
      },
    );
  }
}

class _ErrorView extends StatelessWidget {
  const _ErrorView({
    required this.failure,
    required this.onRetry,
    required this.l10n,
  });

  final Object failure;
  final VoidCallback onRetry;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(l10n.purchaseOrderBuilderLoadError),
          const SizedBox(height: 12),
          FilledButton(
            onPressed: onRetry,
            child: Text(l10n.purchaseOrderBuilderRetry),
          ),
        ],
      ),
    );
  }
}
