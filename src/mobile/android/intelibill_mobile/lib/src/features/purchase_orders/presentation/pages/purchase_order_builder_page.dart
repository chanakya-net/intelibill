import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/purchase_order_builder_controller.dart';
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

  final String target;

  @override
  ConsumerState<PurchaseOrderBuilderPage> createState() =>
      _PurchaseOrderBuilderPageState();
}

class _PurchaseOrderBuilderPageState
    extends ConsumerState<PurchaseOrderBuilderPage> {
  final _referenceController = TextEditingController();
  final _notesController = TextEditingController();

  @override
  void dispose() {
    _referenceController.dispose();
    _notesController.dispose();
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
}

class _BuilderBody extends StatelessWidget {
  const _BuilderBody({
    required this.state,
    required this.referenceController,
    required this.notesController,
    required this.onSupplierChanged,
    required this.onOrderDateChanged,
    required this.onExpectedDeliveryDateChanged,
    required this.onReferenceChanged,
    required this.onNotesChanged,
    required this.onRetry,
    required this.l10n,
  });

  final PurchaseOrderBuilderState state;
  final TextEditingController referenceController;
  final TextEditingController notesController;
  final ValueChanged<Supplier?> onSupplierChanged;
  final ValueChanged<DateTime?> onOrderDateChanged;
  final ValueChanged<DateTime?> onExpectedDeliveryDateChanged;
  final ValueChanged<String> onReferenceChanged;
  final ValueChanged<String> onNotesChanged;
  final VoidCallback onRetry;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    if (state.isLoadingSuppliers && state.suppliers.isEmpty) {
      return const Center(child: CircularProgressIndicator());
    }
    if (state.failure != null && state.suppliers.isEmpty) {
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
              onChanged: onOrderDateChanged,
              l10n: l10n,
            ),
            const SizedBox(height: 16),
            _DateField(
              key: PurchaseOrderBuilderPage.expectedDeliveryDateFieldKey,
              label: l10n.purchaseOrderBuilderExpectedDeliveryDate,
              value: state.expectedDeliveryDate,
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
          ],
        ),
      ),
    );
  }
}

class _DateField extends StatelessWidget {
  const _DateField({
    super.key,
    required this.label,
    required this.value,
    required this.onChanged,
    required this.l10n,
  });

  final String label;
  final DateTime? value;
  final ValueChanged<DateTime?> onChanged;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    return TextFormField(
      readOnly: true,
      initialValue: value == null
          ? ''
          : MaterialLocalizations.of(context).formatMediumDate(value!),
      decoration: InputDecoration(
        labelText: label,
        hintText: l10n.purchaseOrderBuilderSelectDate,
        suffixIcon: const Icon(Icons.calendar_today),
      ),
      onTap: () async {
        final date = await showDatePicker(
          context: context,
          initialDate: value ?? DateTime.now(),
          firstDate: DateTime(2000),
          lastDate: DateTime(2100),
        );
        if (date != null) onChanged(date);
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
