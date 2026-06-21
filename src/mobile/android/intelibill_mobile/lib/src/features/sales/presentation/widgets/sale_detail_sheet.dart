import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/shell/menu_visibility.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_list_item.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/sale_detail_controller.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/widgets/sale_return_sheet.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/utils/sale_display_helpers.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/widgets/void_sale_return_sheet.dart';
import 'package:intl/intl.dart';

Future<void> showSaleDetailSheet(
  BuildContext context, {
  required SaleListItem sale,
}) {
  return showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    useSafeArea: true,
    builder: (context) => SaleDetailSheet(saleId: sale.saleId),
  );
}

class SaleDetailSheet extends ConsumerWidget {
  const SaleDetailSheet({required this.saleId, super.key});

  final String saleId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(saleDetailControllerProvider(saleId));
    final authState = ref.watch(authControllerProvider);
    final canVoidReturns = isOwnerOrManager(authState.value?.session);
    final l10n = AppLocalizations.of(context)!;

    if (state.isLoading) {
      return const SafeArea(
        child: Padding(
          padding: EdgeInsets.all(32),
          child: Center(child: CircularProgressIndicator()),
        ),
      );
    }

    if (state.failure != null) {
      return _ErrorState(
        onRetry: () {
          ref.read(saleDetailControllerProvider(saleId).notifier).refresh();
        },
        message: l10n.salesDetailUnableToLoad,
        retryLabel: l10n.salesHistoryRetry,
      );
    }

    final detail = state.detail;
    if (detail == null) {
      return SafeArea(child: Center(child: Text(l10n.salesHistoryNoSales)));
    }

    return SafeArea(
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(16, 0, 16, 24),
        child: Column(
          children: [
            _Header(
              title: l10n.salesHistoryDetailTitle,
              subtitle: detail.invoiceNumber,
              returnLabel: l10n.salesReturnTitle,
              onReceipt: () => _openReceipt(context, detail),
              onRefresh: () {
                ref
                    .read(saleDetailControllerProvider(saleId).notifier)
                    .refresh();
              },
              onReturn: detail.items.any((item) => item.returnableQuantity > 0)
                  ? () {
                      showSaleReturnSheet(context, saleId: detail.saleId);
                    }
                  : null,
            ),
            const SizedBox(height: 16),
            _SummaryCard(detail: detail),
            const SizedBox(height: 16),
            _SectionCard(
              title: l10n.salesDetailLineItems,
              child: _LineItems(detail: detail),
            ),
            const SizedBox(height: 16),
            _SectionCard(
              title: l10n.salesDetailTotals,
              child: _Totals(detail: detail),
            ),
            const SizedBox(height: 16),
            _SectionCard(
              title: l10n.salesDetailDiscounts,
              child: _Discounts(detail: detail),
            ),
            const SizedBox(height: 16),
            _SectionCard(
              title: l10n.salesDetailPaymentSplit,
              child: _Settlements(detail: detail),
            ),
            const SizedBox(height: 16),
            _SectionCard(
              title: l10n.salesDetailReturns,
              child: _Returns(
                detail: detail,
                canVoid: canVoidReturns,
                onVoidReturn: (saleReturn) {
                  unawaited(_showVoidReturnSheet(context, ref, saleReturn));
                },
              ),
            ),
            const SizedBox(height: 16),
            _SectionCard(
              title: l10n.salesDetailRedemptions,
              child: _Redemptions(detail: detail),
            ),
            const SizedBox(height: 16),
            _SectionCard(
              title: l10n.salesDetailWarnings,
              child: _Warnings(detail: detail),
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _showVoidReturnSheet(
    BuildContext context,
    WidgetRef ref,
    SaleDetailReturn saleReturn,
  ) async {
    final l10n = AppLocalizations.of(context)!;
    final action = await showVoidSaleReturnSheet(
      context,
      saleReturn: saleReturn,
      l10n: l10n,
      onVoid: (reason) async {
        final isSuccess = await ref
            .read(saleDetailControllerProvider(saleId).notifier)
            .voidSaleReturn(
              saleReturnId: saleReturn.saleReturnId,
              reason: reason,
            );
        if (isSuccess) {
          return null;
        }

        return ref.read(saleDetailControllerProvider(saleId)).voidFailure;
      },
    );

    if (!context.mounted || action != true) {
      return;
    }
  }
}

class _Header extends StatelessWidget {
  const _Header({
    required this.title,
    required this.subtitle,
    required this.returnLabel,
    required this.onReceipt,
    required this.onRefresh,
    this.onReturn,
  });

  final String title;
  final String subtitle;
  final String returnLabel;
  final VoidCallback onReceipt;
  final VoidCallback onRefresh;
  final VoidCallback? onReturn;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Row(
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                title,
                style: theme.textTheme.titleLarge?.copyWith(
                  fontWeight: FontWeight.w700,
                ),
              ),
              const SizedBox(height: 2),
              Text(
                subtitle,
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),
            ],
          ),
        ),
        IconButton(icon: const Icon(Icons.refresh), onPressed: onRefresh),
        TextButton.icon(
          key: const Key('sales-detail-receipt-button'),
          icon: const Icon(Icons.receipt_long),
          onPressed: onReceipt,
          label: Text(AppLocalizations.of(context)!.salesDetailReceipt),
        ),
        if (onReturn != null)
          TextButton.icon(
            icon: const Icon(Icons.assignment_return),
            onPressed: onReturn,
            label: Text(returnLabel),
          ),
      ],
    );
  }
}

void _openReceipt(BuildContext context, SaleDetail detail) {
  context.push(AppRoutes.salesReceiptFor(detail.saleId), extra: detail);
}

class _SummaryCard extends StatelessWidget {
  const _SummaryCard({required this.detail});

  final SaleDetail detail;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    final dateFormat = DateFormat('dd MMM yyyy, h:mm a');

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            _InfoRow(
              label: l10n.salesHistoryCustomer,
              value: detail.customerName ?? l10n.salesHistoryWalkInCustomer,
            ),
            if (detail.customerPhone?.isNotEmpty == true)
              _InfoRow(
                label: l10n.salesHistoryPhone,
                value: detail.customerPhone!,
              ),
            _InfoRow(
              label: l10n.salesHistoryDate,
              value: dateFormat.format(detail.soldAt),
            ),
            _InfoRow(
              label: l10n.salesHistoryPaymentMethod,
              value: salePaymentMethodLabel(l10n, detail.paymentMethod),
            ),
            if (detail.status != null && detail.status!.isNotEmpty)
              _InfoRow(
                label: l10n.salesHistoryControlsStatus,
                value: saleStatusLabel(l10n, detail.status!),
              ),
            const Divider(height: 24),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                _MetricChip(
                  label:
                      '${l10n.salesDetailPaidLabel} ${formatInr(detail.paidAmount)}',
                ),
                _MetricChip(
                  label:
                      '${l10n.salesHistoryDueAmount} ${formatInr(detail.dueAmount)}',
                ),
                _MetricChip(
                  label:
                      '${l10n.salesDetailTaxLabel} ${formatInr(detail.totalTaxAmount)}',
                ),
              ],
            ),
            const SizedBox(height: 12),
            Text(
              formatInr(detail.totalAmount),
              style: theme.textTheme.headlineSmall?.copyWith(
                fontWeight: FontWeight.w800,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _SectionCard extends StatelessWidget {
  const _SectionCard({required this.title, required this.child});

  final String title;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              title,
              style: theme.textTheme.titleMedium?.copyWith(
                fontWeight: FontWeight.w700,
              ),
            ),
            const SizedBox(height: 12),
            child,
          ],
        ),
      ),
    );
  }
}

class _LineItems extends StatelessWidget {
  const _LineItems({required this.detail});

  final SaleDetail detail;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    if (detail.items.isEmpty) {
      return Text(AppLocalizations.of(context)!.salesDetailNoLineItems);
    }
    return Column(
      children: [
        for (final item in detail.items) ...[
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      item.name,
                      style: theme.textTheme.bodyMedium?.copyWith(
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    Text(
                      '${item.quantity} × ${formatInr(item.rate)}',
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: theme.colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
              ),
              Text(
                formatInr(item.total),
                style: theme.textTheme.bodyMedium?.copyWith(
                  fontWeight: FontWeight.w600,
                ),
              ),
            ],
          ),
          const SizedBox(height: 10),
        ],
      ],
    );
  }
}

class _Totals extends StatelessWidget {
  const _Totals({required this.detail});

  final SaleDetail detail;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final redemptionTotal = detail.redemptions.fold<double>(
      0.0,
      (sum, redemption) => sum + redemption.amount,
    );
    final showRefundAmount =
        detail.refundAmount > 0 &&
        (redemptionTotal - detail.refundAmount).abs() > 0.01;

    return Column(
      children: [
        _InfoRow(
          label: l10n.salesDetailBeforeDiscount,
          value: formatInr(detail.totalBeforeDiscount),
        ),
        _InfoRow(
          label: l10n.salesDetailDiscountLabel,
          value: formatInr(detail.totalDiscountAmount),
        ),
        _InfoRow(
          label: l10n.salesDetailTaxLabel,
          value: formatInr(detail.totalTaxAmount),
        ),
        _InfoRow(
          label: l10n.salesHistoryTotal,
          value: formatInr(detail.totalAmount),
        ),
        _InfoRow(
          label: l10n.salesDetailPaidLabel,
          value: formatInr(detail.paidAmount),
        ),
        _InfoRow(
          label: l10n.salesHistoryDueAmount,
          value: formatInr(detail.dueAmount),
        ),
        if (showRefundAmount)
          _InfoRow(
            label: l10n.salesHistoryRefundAmount,
            value: formatInr(detail.refundAmount),
          ),
        if (detail.dueReductionAmount > 0)
          _InfoRow(
            label: l10n.salesDetailDueReduction,
            value: formatInr(detail.dueReductionAmount),
          ),
      ],
    );
  }
}

class _Discounts extends StatelessWidget {
  const _Discounts({required this.detail});

  final SaleDetail detail;

  @override
  Widget build(BuildContext context) {
    if (detail.discounts.isEmpty) {
      return Text(AppLocalizations.of(context)!.salesDetailNoDiscounts);
    }
    final theme = Theme.of(context);
    return Column(
      children: [
        for (final discount in detail.discounts) ...[
          Row(
            children: [
              Expanded(
                child: Text(
                  '${discount.type} • ${discount.value}',
                  style: theme.textTheme.bodyMedium?.copyWith(
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ),
              Text('- ${formatInr(discount.amount)}'),
            ],
          ),
          const SizedBox(height: 10),
        ],
      ],
    );
  }
}

class _Settlements extends StatelessWidget {
  const _Settlements({required this.detail});

  final SaleDetail detail;

  @override
  Widget build(BuildContext context) {
    if (detail.settlements.isEmpty) {
      return Text(AppLocalizations.of(context)!.salesDetailNoSettlementRecords);
    }
    final theme = Theme.of(context);
    final dateFormat = DateFormat('dd MMM yyyy, h:mm a');
    return Column(
      children: [
        for (final settlement in detail.settlements) ...[
          Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      settlement.method,
                      style: theme.textTheme.bodyMedium?.copyWith(
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    Text(
                      dateFormat.format(settlement.settledAt),
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: theme.colorScheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
              ),
              Text(formatInr(settlement.amount)),
            ],
          ),
          const SizedBox(height: 10),
        ],
      ],
    );
  }
}

class _Returns extends StatelessWidget {
  const _Returns({
    required this.detail,
    required this.canVoid,
    required this.onVoidReturn,
  });

  final SaleDetail detail;
  final bool canVoid;
  final void Function(SaleDetailReturn saleReturn) onVoidReturn;

  @override
  Widget build(BuildContext context) {
    if (detail.returns.isEmpty) {
      return Text(AppLocalizations.of(context)!.salesDetailNoReturns);
    }
    final theme = Theme.of(context);
    final l10n = AppLocalizations.of(context)!;
    final dateFormat = DateFormat('dd MMM yyyy, h:mm a');
    return Column(
      children: [
        for (final saleReturn in detail.returns) ...[
          Row(
            children: [
              Expanded(
                child: Text(
                  saleReturn.returnNumber,
                  style: theme.textTheme.bodyMedium?.copyWith(
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ),
              Text(formatInr(saleReturn.amount)),
            ],
          ),
          Text(
            dateFormat.format(saleReturn.returnedAt),
            style: theme.textTheme.bodySmall?.copyWith(
              color: theme.colorScheme.onSurfaceVariant,
            ),
          ),
          if (saleReturn.voidedAt != null)
            Text(
              '${l10n.salesDetailVoidReturnDate} '
              '${dateFormat.format(saleReturn.voidedAt!)}',
              style: theme.textTheme.bodySmall,
            ),
          if (saleReturn.voidReason != null &&
              saleReturn.voidReason!.trim().isNotEmpty)
            Text(
              '${l10n.salesDetailVoidReturnReason}: ${saleReturn.voidReason}',
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.error,
              ),
            ),
          if (saleReturn.isVoided)
            Text(
              l10n.salesDetailReturnVoided,
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.error,
                fontWeight: FontWeight.w600,
              ),
            ),
          if (!saleReturn.isVoided && canVoid)
            Align(
              alignment: Alignment.centerRight,
              child: TextButton(
                key: Key(
                  voidReturnActionKey('button', saleReturn.saleReturnId),
                ),
                onPressed: () => onVoidReturn(saleReturn),
                child: Text(l10n.salesDetailVoidReturnAction),
              ),
            ),
          if (saleReturn.items.isNotEmpty)
            Padding(
              padding: const EdgeInsets.only(top: 6),
              child: Column(
                children: [
                  for (final item in saleReturn.items)
                    Align(
                      alignment: Alignment.centerLeft,
                      child: Text(
                        l10n.salesDetailReturnItem(
                          item.quantity,
                          item.itemName ?? item.itemId,
                        ),
                      ),
                    ),
                ],
              ),
            ),
          const SizedBox(height: 10),
        ],
      ],
    );
  }
}

class _Redemptions extends StatelessWidget {
  const _Redemptions({required this.detail});

  final SaleDetail detail;

  @override
  Widget build(BuildContext context) {
    if (detail.redemptions.isEmpty) {
      return Text(AppLocalizations.of(context)!.salesDetailNoRedemptions);
    }
    final theme = Theme.of(context);
    return Column(
      children: [
        for (final redemption in detail.redemptions) ...[
          Row(
            children: [
              Expanded(
                child: Text(
                  redemption.code,
                  style: theme.textTheme.bodyMedium?.copyWith(
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ),
              Text(formatInr(redemption.amount)),
            ],
          ),
          const SizedBox(height: 10),
        ],
      ],
    );
  }
}

class _Warnings extends StatelessWidget {
  const _Warnings({required this.detail});

  final SaleDetail detail;

  @override
  Widget build(BuildContext context) {
    if (detail.warnings.isEmpty) {
      return Text(AppLocalizations.of(context)!.salesDetailNoWarnings);
    }
    final theme = Theme.of(context);
    return Column(
      children: [
        for (final warning in detail.warnings) ...[
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(
                Icons.warning_amber_rounded,
                size: 20,
                color: theme.colorScheme.error,
              ),
              const SizedBox(width: 8),
              Expanded(child: Text(warning)),
            ],
          ),
          const SizedBox(height: 10),
        ],
      ],
    );
  }
}

class _ErrorState extends StatelessWidget {
  const _ErrorState({
    required this.onRetry,
    required this.message,
    required this.retryLabel,
  });

  final VoidCallback onRetry;
  final String message;
  final String retryLabel;

  @override
  Widget build(BuildContext context) {
    return SafeArea(
      child: Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.error_outline, size: 48),
              const SizedBox(height: 12),
              Text(message, textAlign: TextAlign.center),
              const SizedBox(height: 16),
              FilledButton(onPressed: onRetry, child: Text(retryLabel)),
            ],
          ),
        ),
      ),
    );
  }
}

class _InfoRow extends StatelessWidget {
  const _InfoRow({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            flex: 2,
            child: Text(
              label,
              style: theme.textTheme.bodyMedium?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
          ),
          Expanded(
            flex: 3,
            child: Text(
              value,
              style: theme.textTheme.bodyMedium?.copyWith(
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _MetricChip extends StatelessWidget {
  const _MetricChip({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
      decoration: BoxDecoration(
        color: theme.colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(label, style: theme.textTheme.labelMedium),
    );
  }
}
