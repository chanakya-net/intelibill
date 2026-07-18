import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/controllers/receive_purchase_order_controller.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/widgets/purchase_order_receive_line_card.dart';
import 'package:intl/intl.dart';

class ReceivePurchaseOrderPage extends ConsumerWidget {
  const ReceivePurchaseOrderPage({required this.purchaseOrderId, super.key});

  static const pageKey = Key('purchase-order-receive-page');
  static const receiveButtonKey = Key('purchase-order-receive-submit-button');
  static const referenceFieldKey = Key('purchase-order-receive-reference');
  static const notesFieldKey = Key('purchase-order-receive-notes');
  static const noLinesTextKey = Key('purchase-order-receive-no-lines');

  final String purchaseOrderId;

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
          message: state.failure!.message ?? l10n.purchaseOrderReceiveRetry,
          onRetry: () => controller.refresh(),
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
            if (state.failure != null) ...[
              const SizedBox(height: 8),
              Text(
                state.failure!.message ??
                    l10n.purchaseOrderReceiveSubmitFailure,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
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
                  onBarcodeChanged: (value) =>
                      controller.updateBarcode(line.purchaseOrderLineId, value),
                  onBatchNumberChanged: (value) => controller.updateBatchNumber(
                    line.purchaseOrderLineId,
                    value,
                  ),
                ),
              ),
            const SizedBox(height: 12),
            _SummaryCard(lines: state.lines),
          ],
        ),
      ),
      bottomNavigationBar: SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 12),
          child: FilledButton(
            key: receiveButtonKey,
            onPressed: !state.canSubmit
                ? null
                : () => _submit(context, controller, l10n),
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
    AppLocalizations l10n,
  ) async {
    try {
      await controller.submit();
      if (context.mounted) {
        context.go(AppRoutes.purchaseOrderDetailFor(purchaseOrderId));
      }
    } on AppException {
      if (!context.mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(l10n.purchaseOrderReceiveSubmitFailure)),
      );
    }
  }
}

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
            Text(message),
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
  const _SummaryCard({required this.lines});

  final List<ReceivePurchaseOrderLineDraft> lines;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final totalQty = lines.fold<double>(0, (sum, line) => sum + line.quantity);
    final totalExpectedPurchaseCost = lines.fold<double>(
      0,
      (sum, line) => sum + line.totalPurchaseCost,
    );
    return Card(
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
            Text('${l10n.purchaseOrderReceiveLineCount}: ${lines.length}'),
            Text(
              '${l10n.purchaseOrderReceiveQuantity}: ${totalQty.toStringAsFixed(0)}',
            ),
            Text(
              '${l10n.purchaseOrderReceiveTotalExpectedPurchaseCost}: ${formatInr(totalExpectedPurchaseCost)}',
            ),
          ],
        ),
      ),
    );
  }
}
