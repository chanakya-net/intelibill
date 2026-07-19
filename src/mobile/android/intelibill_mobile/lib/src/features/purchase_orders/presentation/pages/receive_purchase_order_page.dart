import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/receive_purchase_order_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/localization/purchase_order_messages.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_receive_line_card.dart';
import 'package:intelibill_mobile/src/shared/barcode_scanner/barcode_scan_result.dart';
import 'package:intelibill_mobile/src/shared/barcode_scanner/show_barcode_scanner.dart';
import 'package:intl/intl.dart';

class ReceivePurchaseOrderPage extends ConsumerWidget {
  const ReceivePurchaseOrderPage({
    required this.purchaseOrderId,
    this.scanBarcode,
    super.key,
  });

  static const pageKey = Key('purchase-order-receive-page');
  static const receiveButtonKey = Key('purchase-order-receive-submit-button');
  static const referenceFieldKey = Key('purchase-order-receive-reference');
  static const notesFieldKey = Key('purchase-order-receive-notes');
  static const noLinesTextKey = Key('purchase-order-receive-no-lines');

  final String purchaseOrderId;
  final Future<BarcodeScanResult?> Function(BuildContext context)? scanBarcode;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context)!;
    final controller = ref.read(
      receivePurchaseOrderControllerProvider(purchaseOrderId).notifier,
    );
    final state = ref.watch(
      receivePurchaseOrderControllerProvider(purchaseOrderId),
    );

    if (state.isLoading && state.detail == null) {
      return Scaffold(
        key: pageKey,
        appBar: AppBar(title: Text(l10n.purchaseOrderReceiveTitle)),
        body: const Center(child: CircularProgressIndicator()),
      );
    }

    if (state.failure != null && state.detail == null) {
      return Scaffold(
        key: pageKey,
        appBar: AppBar(title: Text(l10n.purchaseOrderReceiveTitle)),
        body: _FailureView(
          message: purchaseOrderFailureMessage(l10n, state.failure!),
          onRetry: controller.refresh,
        ),
      );
    }

    final detail = state.detail;
    if (detail == null) {
      return const Scaffold(key: pageKey, body: SizedBox.shrink());
    }

    final receivedAt = state.receivedAt ?? DateTime.now().toUtc();
    final locale = Localizations.localeOf(context).toLanguageTag();
    final receivedAtText = DateFormat.yMMMEd(locale).add_jm().format(
      receivedAt.toLocal(),
    );

    return Scaffold(
      key: pageKey,
      appBar: AppBar(title: Text(l10n.purchaseOrderReceiveTitle)),
      body: SafeArea(
        child: FocusTraversalGroup(
          policy: OrderedTraversalPolicy(),
          child: ListView(
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
            children: [
              Text(
                detail.purchaseOrderNumber,
                style: Theme.of(context).textTheme.titleLarge,
              ),
              const SizedBox(height: 8),
              _ReadOnlyField(
                label: l10n.purchaseOrderReceiveReceivedAtLabel,
                value: receivedAtText,
              ),
              const SizedBox(height: 12),
              TextFormField(
                key: referenceFieldKey,
                decoration: InputDecoration(
                  labelText: l10n.purchaseOrderReceiveReferenceLabel,
                ),
                initialValue: state.referenceNumber,
                onChanged: controller.updateReferenceNumber,
                maxLength: 120,
              ),
              const SizedBox(height: 12),
              TextFormField(
                key: notesFieldKey,
                decoration: InputDecoration(
                  labelText: l10n.purchaseOrderReceiveNotesLabel,
                ),
                initialValue: state.notes,
                onChanged: controller.updateNotes,
                maxLines: 4,
                maxLength: 500,
              ),
              if (state.failure != null && state.lineErrors.isEmpty) ...[
                const SizedBox(height: 8),
                Semantics(
                  liveRegion: true,
                  child: Text(
                    state.localMessage == null
                        ? purchaseOrderFailureMessage(l10n, state.failure!)
                        : purchaseOrderMessage(l10n, state.localMessage!),
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                    ),
                  ),
                ),
              ],
              const SizedBox(height: 12),
              if (state.lines.isEmpty)
                Padding(
                  key: noLinesTextKey,
                  padding: const EdgeInsets.only(bottom: 12),
                  child: Text(l10n.purchaseOrderReceiveNoLines),
                )
              else
                ...state.lines.map(
                  (line) => PurchaseOrderReceiveLineCard(
                    line: line,
                    onSelectionChanged: (value) => controller.setLineSelected(
                      line.purchaseOrderLineId,
                      isSelected: value,
                    ),
                    onQuantityChanged: (value) => controller.updateQuantity(
                      line.purchaseOrderLineId,
                      value,
                    ),
                    onBarcodeChanged: (value) => controller.updateBarcode(
                      line.purchaseOrderLineId,
                      value,
                    ),
                    onScanBarcode: () => _scanLineBarcode(
                      context,
                      ref,
                      l10n,
                      line.purchaseOrderLineId,
                    ),
                    onGenerateBarcode: () => _generateLineBarcode(
                      context,
                      ref,
                      l10n,
                      line.purchaseOrderLineId,
                    ),
                    onBatchNumberChanged: (value) =>
                        controller.updateBatchNumber(
                          line.purchaseOrderLineId,
                          value,
                        ),
                    onUnitPurchaseCostChanged: (value) =>
                        controller.updateUnitPurchaseCost(
                          line.purchaseOrderLineId,
                          value,
                        ),
                    onTotalPurchaseCostChanged: (value) =>
                        controller.updateTotalPurchaseCost(
                          line.purchaseOrderLineId,
                          value,
                        ),
                    onMrpChanged: (value) => controller.updateMrp(
                      line.purchaseOrderLineId,
                      value,
                    ),
                    onSalesPriceChanged: (value) => controller.updateSalesPrice(
                      line.purchaseOrderLineId,
                      value,
                    ),
                    onTaxRatePercentChanged: (value) =>
                        controller.updateTaxRatePercent(
                          line.purchaseOrderLineId,
                          value,
                        ),
                    onTaxIncludedChanged: (value) =>
                        controller.updateTaxIncluded(
                          line.purchaseOrderLineId,
                          value: value,
                        ),
                    onPurchaseTaxIncludedChanged: (value) =>
                        controller.updatePurchaseTaxIncluded(
                          line.purchaseOrderLineId,
                          value: value,
                        ),
                    onExpiryDateChanged: (value) => controller.updateExpiryDate(
                      line.purchaseOrderLineId,
                      value,
                    ),
                    onManufacturingDateChanged: (value) =>
                        controller.updateManufacturingDate(
                          line.purchaseOrderLineId,
                          value,
                        ),
                    isExpanded:
                        state.expandedLineId == line.purchaseOrderLineId,
                    focusedField:
                        state.focusedLineId == null ||
                            state.expandedLineId != line.purchaseOrderLineId
                        ? null
                        : state.focusedLineId,
                    errors: _localizedLineErrors(
                      l10n,
                      state.lineErrors[line.purchaseOrderLineId],
                    ),
                    isBarcodeGenerating: state.barcodeGenerationLineIds
                        .contains(
                          line.purchaseOrderLineId,
                        ),
                    barcodeGenerationFailure: _localizedOptionalMessage(
                      l10n,
                      state.barcodeGenerationFailures[line.purchaseOrderLineId],
                    ),
                    isPrefillLoading: state.prefillLoadingLineIds.contains(
                      line.purchaseOrderLineId,
                    ),
                    prefillFailure: _localizedOptionalMessage(
                      l10n,
                      state.prefillFailures[line.purchaseOrderLineId],
                    ),
                  ),
                ),
              const SizedBox(height: 12),
              _SummaryCard(state: state),
            ],
          ),
        ),
      ),
      bottomNavigationBar: SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 12),
          child: FilledButton(
            key: receiveButtonKey,
            onPressed: !state.canSubmit
                ? null
                : () => _submit(context, controller),
            child: state.isSubmitting
                ? const SizedBox(
                    height: 20,
                    width: 20,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : Text(l10n.purchaseOrderReceiveSubmit),
          ),
        ),
      ),
    );
  }

  Future<void> _submit(
    BuildContext context,
    ReceivePurchaseOrderController controller,
  ) async {
    try {
      final updated = await controller.submit();
      if (updated != null && context.mounted) {
        context.go(AppRoutes.purchaseOrderDetailFor(purchaseOrderId));
      }
    } on AppException {
      // Controller state owns the localized inline failure presentation.
    }
  }

  Future<void> _scanLineBarcode(
    BuildContext context,
    WidgetRef ref,
    AppLocalizations l10n,
    String lineId,
  ) async {
    final scanner = scanBarcode ?? showBarcodeScanner;
    final result = await scanner(context);
    if (result == null || result.value.trim().isEmpty || !context.mounted) {
      return;
    }
    final current = _line(ref, lineId);
    if (current == null) return;

    final nextBarcode = result.value.trim();
    final shouldApply = await _confirmBarcodeChange(
      context: context,
      l10n: l10n,
      existing: current.barcode,
      next: nextBarcode,
    );
    if (!shouldApply || !context.mounted) return;

    ref
        .read(receivePurchaseOrderControllerProvider(purchaseOrderId).notifier)
        .updateBarcode(lineId, nextBarcode);
  }

  Future<void> _generateLineBarcode(
    BuildContext context,
    WidgetRef ref,
    AppLocalizations l10n,
    String lineId,
  ) async {
    final controller = ref.read(
      receivePurchaseOrderControllerProvider(purchaseOrderId).notifier,
    );
    final generated = await controller.generateItemBarcodeForLine(lineId);
    if (generated == null || generated.trim().isEmpty || !context.mounted) {
      return;
    }

    final current = _line(ref, lineId);
    if (current == null) return;

    final shouldApply = await _confirmBarcodeChange(
      context: context,
      l10n: l10n,
      existing: current.barcode,
      next: generated,
    );
    if (!shouldApply || !context.mounted) return;

    controller.applyGeneratedBarcode(lineId, generated);
  }

  ReceivePurchaseOrderLineDraft? _line(WidgetRef ref, String lineId) {
    final lines = ref
        .read(receivePurchaseOrderControllerProvider(purchaseOrderId))
        .lines;
    for (final line in lines) {
      if (line.purchaseOrderLineId == lineId) return line;
    }
    return null;
  }

  Future<bool> _confirmBarcodeChange({
    required BuildContext context,
    required AppLocalizations l10n,
    required String existing,
    required String next,
  }) async {
    if (existing.trim().isEmpty || existing.trim() == next) {
      return true;
    }
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        content: Text(
          l10n.purchaseOrderReceiveBarcodeReplaceConfirm(
            existing.trim(),
            next,
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(false),
            child: Text(l10n.commonCancel),
          ),
          FilledButton(
            onPressed: () => Navigator.of(context).pop(true),
            child: Text(l10n.purchaseOrderReceiveBarcodeReplaceConfirmLabel),
          ),
        ],
      ),
    );
    return confirmed ?? false;
  }
}

Map<String, String> _localizedLineErrors(
  AppLocalizations l10n,
  Map<String, PurchaseOrderFieldMessage>? errors,
) =>
    errors?.map(
      (field, message) => MapEntry(
        field,
        purchaseOrderMessage(
          l10n,
          message.code,
          maxLength: message.maxLength,
        ),
      ),
    ) ??
    const {};

String? _localizedOptionalMessage(
  AppLocalizations l10n,
  PurchaseOrderMessage? message,
) => message == null ? null : purchaseOrderMessage(l10n, message);

class _FailureView extends StatelessWidget {
  const _FailureView({required this.message, required this.onRetry});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Semantics(liveRegion: true, child: Text(message)),
            const SizedBox(height: 12),
            FilledButton(
              onPressed: onRetry,
              child: Text(l10n.purchaseOrderReceiveRetry),
            ),
          ],
        ),
      ),
    );
  }
}

class _ReadOnlyField extends StatelessWidget {
  const _ReadOnlyField({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(label, style: Theme.of(context).textTheme.labelLarge),
        const SizedBox(height: 4),
        Text(value),
      ],
    );
  }
}

class _SummaryCard extends StatelessWidget {
  const _SummaryCard({required this.state});

  final ReceivePurchaseOrderState state;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    return Semantics(
      container: true,
      label: l10n.purchaseOrderReceiveSummary,
      child: Card(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                l10n.purchaseOrderReceiveSummary,
                style: Theme.of(context).textTheme.titleMedium,
              ),
              const SizedBox(height: 8),
              Text(
                '${l10n.purchaseOrderReceiveLineCount}: ${state.selectedLineCount}',
              ),
              Text(
                '${l10n.purchaseOrderReceiveQuantity}: ${state.selectedQuantity}',
              ),
              Text(
                '${l10n.purchaseOrderReceiveTotalExpectedPurchaseCost}: ${formatInr(state.selectedPurchaseCost)}',
              ),
            ],
          ),
        ),
      ),
    );
  }
}
