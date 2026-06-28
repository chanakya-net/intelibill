import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_return.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sale_return_controller.dart';

Future<void> showSaleReturnSheet(
  BuildContext context, {
  required String saleId,
}) {
  return showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    useSafeArea: true,
    builder: (_) => SaleReturnSheet(saleId: saleId),
  );
}

class SaleReturnSheet extends ConsumerWidget {
  const SaleReturnSheet({required this.saleId, super.key});

  final String saleId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final notifier = ref.read(saleReturnControllerProvider(saleId).notifier);
    final state = ref.watch(saleReturnControllerProvider(saleId));
    final l10n = AppLocalizations.of(context)!;

    if (state.detail == null) {
      return const SafeArea(child: Center(child: CircularProgressIndicator()));
    }

    final itemsById = {
      for (final item in state.detail!.items) item.saleItemId: item,
    };

    if (!state.hasAnyReturnable) {
      return SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Text(l10n.salesReturnNoReturnableLines),
        ),
      );
    }

    return SafeArea(
      child: Padding(
        padding: EdgeInsets.only(
          left: 16,
          right: 16,
          top: 16,
          bottom: 24 + MediaQuery.viewInsetsOf(context).bottom,
        ),
        child: ListView(
          shrinkWrap: true,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    l10n.salesReturnTitle,
                    style: Theme.of(context).textTheme.titleLarge,
                  ),
                ),
                IconButton(
                  icon: const Icon(Icons.close),
                  onPressed: () => Navigator.of(context).pop(),
                ),
              ],
            ),
            const SizedBox(height: 12),
            for (final draft in state.drafts)
              _ReturnLineTile(
                draft: draft,
                item: itemsById[draft.saleItemId],
                l10n: l10n,
                onToggle: (value) =>
                    notifier.toggleLine(draft.saleItemId, selected: value),
                onQuantityChanged: (value) => notifier.updateQuantity(
                  draft.saleItemId,
                  double.tryParse(value) ?? 0,
                ),
                onConditionChanged: (condition) =>
                    notifier.updateCondition(draft.saleItemId, condition),
                onApprovedRefundChanged: (value) => notifier
                    .updateApprovedRefundAmount(draft.saleItemId, value),
                onLineNotesChanged: (value) =>
                    notifier.updateLineNotes(draft.saleItemId, value),
              ),
            const SizedBox(height: 12),
            _DueSection(state: state, notifier: notifier, l10n: l10n),
            if (state.preview != null) ...[
              const SizedBox(height: 12),
              _PreviewCard(preview: state.preview!, l10n: l10n),
              const SizedBox(height: 12),
              _PayoutSection(state: state, notifier: notifier, l10n: l10n),
            ],
            const SizedBox(height: 12),
            if (state.failure != null)
              Text(
                _failureMessage(state.failure!),
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: FilledButton.icon(
                    onPressed:
                        state.isPreviewLoading || !state.canPreviewReturns
                        ? null
                        : notifier.preview,
                    icon: state.isPreviewLoading
                        ? const SizedBox(
                            width: 16,
                            height: 16,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.analytics_outlined),
                    label: Text(l10n.salesReturnPreview),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: FilledButton.icon(
                    onPressed: state.canSubmitReturns && !state.isSubmitting
                        ? () async {
                            await notifier.submit();
                            final latestState = ref.read(
                              saleReturnControllerProvider(saleId),
                            );
                            if (context.mounted &&
                                latestState.failure == null) {
                              Navigator.of(context).pop();
                            }
                          }
                        : null,
                    icon: state.isSubmitting
                        ? const SizedBox(
                            width: 16,
                            height: 16,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.check_circle_outline),
                    label: Text(l10n.salesReturnSubmit),
                  ),
                ),
              ],
            ),
            if (!state.canSubmitReturns)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Text(
                  l10n.salesReturnSubmitRoleNotice,
                  style: TextStyle(
                    color: Theme.of(context).colorScheme.onSurfaceVariant,
                    fontSize: 12,
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _ReturnLineTile extends StatelessWidget {
  const _ReturnLineTile({
    required this.item,
    required this.draft,
    required this.l10n,
    required this.onToggle,
    required this.onQuantityChanged,
    required this.onConditionChanged,
    required this.onApprovedRefundChanged,
    required this.onLineNotesChanged,
  });

  static const _serviceLineType = 'Service';

  final SaleDetailItem? item;
  final SaleReturnLineDraft draft;
  final AppLocalizations l10n;
  final ValueChanged<bool> onToggle;
  final ValueChanged<String> onQuantityChanged;
  final ValueChanged<int?> onConditionChanged;
  final ValueChanged<String> onApprovedRefundChanged;
  final ValueChanged<String> onLineNotesChanged;

  @override
  Widget build(BuildContext context) {
    if (item == null) {
      return const SizedBox.shrink();
    }

    final isGoods = item!.lineType != _serviceLineType;
    return Card(
      margin: const EdgeInsets.only(bottom: 8),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            CheckboxListTile(
              value: draft.selected,
              onChanged: (value) => onToggle(value ?? false),
              controlAffinity: ListTileControlAffinity.leading,
              contentPadding: EdgeInsets.zero,
              title: Text(item!.itemName),
              subtitle: Text(
                '${l10n.salesReturnLineQuantityLabel}: ${item!.returnableQuantity}',
              ),
            ),
            if (draft.selected) ...[
              const SizedBox(height: 8),
              TextFormField(
                keyboardType: const TextInputType.numberWithOptions(
                  decimal: true,
                ),
                key: ValueKey('quantity-${draft.saleItemId}'),
                decoration: InputDecoration(
                  labelText: l10n.salesReturnLineQuantityLabel,
                ),
                initialValue: draft.quantity == 0
                    ? ''
                    : draft.quantity.toString(),
                onChanged: onQuantityChanged,
              ),
              if (isGoods) ...[
                const SizedBox(height: 8),
                DropdownButtonFormField<int>(
                  initialValue: draft.condition,
                  items: [
                    DropdownMenuItem(
                      value: 1,
                      child: Text(l10n.salesReturnLineConditionRestockable),
                    ),
                    DropdownMenuItem(
                      value: 2,
                      child: Text(l10n.salesReturnLineConditionWastage),
                    ),
                  ],
                  onChanged: onConditionChanged,
                  decoration: InputDecoration(
                    labelText: l10n.salesReturnLineConditionLabel,
                  ),
                ),
              ],
              const SizedBox(height: 8),
              TextFormField(
                keyboardType: const TextInputType.numberWithOptions(
                  decimal: true,
                ),
                decoration: InputDecoration(
                  labelText: l10n.salesReturnApprovedRefundLabel,
                ),
                initialValue: draft.approvedRefundAmount?.toString() ?? '',
                onChanged: onApprovedRefundChanged,
              ),
              const SizedBox(height: 8),
              TextFormField(
                decoration: InputDecoration(
                  labelText: l10n.salesReturnLineNoteLabel,
                ),
                initialValue: draft.notes ?? '',
                onChanged: onLineNotesChanged,
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _DueSection extends StatelessWidget {
  const _DueSection({
    required this.state,
    required this.notifier,
    required this.l10n,
  });

  final SaleReturnState state;
  final SaleReturnController notifier;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            TextFormField(
              keyboardType: const TextInputType.numberWithOptions(
                decimal: true,
              ),
              decoration: InputDecoration(
                labelText: l10n.salesReturnDueOverrideLabel,
              ),
              onChanged: notifier.updateDueReductionAmount,
            ),
            const SizedBox(height: 8),
            TextFormField(
              decoration: InputDecoration(
                labelText: l10n.salesReturnDueOverrideReasonLabel,
              ),
              onChanged: notifier.updateDueOverrideReason,
            ),
            const SizedBox(height: 8),
            Row(
              children: [
                Checkbox(
                  value: state.dueOverrideConfirmed,
                  onChanged: state.dueReductionOverrideAmount == null
                      ? null
                      : (value) => notifier.updateDueOverrideConfirmed(
                          value: value ?? false,
                        ),
                ),
                Text(l10n.salesReturnDueOverrideConfirmLabel),
              ],
            ),
            const SizedBox(height: 8),
            TextFormField(
              decoration: InputDecoration(
                labelText: l10n.salesReturnNotesLabel,
              ),
              maxLines: 2,
              onChanged: notifier.updateNotes,
            ),
          ],
        ),
      ),
    );
  }
}

class _PayoutSection extends StatelessWidget {
  const _PayoutSection({
    required this.state,
    required this.notifier,
    required this.l10n,
  });

  static const int _destinationCreditNote = 1;
  static const int _destinationRefund = 2;

  final SaleReturnState state;
  final SaleReturnController notifier;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            DropdownButtonFormField<int>(
              key: const ValueKey('sales-return-payout-destination'),
              initialValue: state.payoutDestination,
              decoration: InputDecoration(
                labelText: l10n.salesReturnPayoutDestinationLabel,
              ),
              items: [
                DropdownMenuItem(
                  value: _destinationRefund,
                  child: Text(l10n.salesReturnDestinationRefund),
                ),
                DropdownMenuItem(
                  value: _destinationCreditNote,
                  child: Text(l10n.salesReturnDestinationCreditNote),
                ),
              ],
              onChanged: notifier.updatePayoutDestination,
            ),
            const SizedBox(height: 8),
            if (state.payoutDestination == _destinationCreditNote) ...[
              TextField(
                decoration: InputDecoration(
                  labelText: l10n.salesReturnCreditNoteReasonLabel,
                ),
                onChanged: notifier.updateCreditNoteReason,
              ),
              const SizedBox(height: 8),
              TextField(
                decoration: InputDecoration(
                  labelText: l10n.salesReturnCreditNoteExpiryLabel,
                ),
                keyboardType: TextInputType.datetime,
                key: const ValueKey('sales-return-credit-note-expiry'),
                onChanged: (value) {
                  final parsed = DateTime.tryParse(value);
                  notifier.updateCreditNoteExpiresAt(parsed);
                },
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _PreviewCard extends StatelessWidget {
  const _PreviewCard({
    required this.preview,
    required this.l10n,
  });

  final SaleReturnPreview preview;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final financial = preview.financial;
    if (financial == null) {
      return const SizedBox.shrink();
    }

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              '${l10n.salesReturnPreviewRefundLabel}: ${formatInr(
                financial.totalRefundAmount,
              )}',
            ),
            Text(
              '${l10n.salesReturnPreviewDueReductionLabel}: ${formatInr(
                financial.dueReductionAmount,
              )}',
            ),
            Text(
              '${l10n.salesReturnPreviewPayoutLabel}: ${formatInr(
                financial.payoutAmount,
              )}',
            ),
            if (preview.warnings.isNotEmpty) ...[
              const SizedBox(height: 8),
              ...preview.warnings.map((item) => Text('• $item')),
            ],
          ],
        ),
      ),
    );
  }
}

String _failureMessage(Failure failure) {
  return failure.when(
    validation: (message, _) => message ?? 'Invalid input.',
    unauthorized: (message) => message ?? 'Authentication expired.',
    forbidden: (message) => message ?? 'Action is not allowed.',
    notFound: (message) => message ?? 'Requested item not found.',
    server: (message, statusCode) => message ?? 'Server error.',
    network: (message) => message ?? 'Network error.',
    timeout: (message) => message ?? 'Request timed out.',
    serialization: (message) => message ?? 'Data parse error.',
    unknown: (message) => message ?? 'An unknown error occurred.',
  );
}
