import 'dart:async';

import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_preview.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sellable.dart';

typedef OnDecrease = void Function(String sellableId);
typedef OnIncrease = void Function(String sellableId);
typedef OnRemove = void Function(String sellableId);
typedef OnUnitPriceChanged = void Function(String sellableId, double value);
typedef OnSubmitCheckout = Future<void> Function();

class GoodsCartList extends StatelessWidget {
  const GoodsCartList({
    super.key,
    required this.lines,
    required this.onDecrease,
    required this.onIncrease,
    required this.onRemove,
    required this.onUnitPriceChanged,
    required this.total,
    required this.subtotal,
    required this.tax,
    required this.discount,
    required this.discountCapacity,
    required this.preview,
    required this.previewFailure,
    required this.isPreviewLoading,
    required this.canSubmitCheckout,
    required this.onRefreshPreview,
    required this.isSubmitting,
    required this.onSubmit,
    required this.recordedSale,
    required this.onViewRecordedSale,
    required this.onViewRecordedReceipt,
    required this.onClearRecordedSale,
  });

  final List<NewSaleCartLine> lines;
  final OnDecrease onDecrease;
  final OnIncrease onIncrease;
  final OnRemove onRemove;
  final OnUnitPriceChanged onUnitPriceChanged;
  final double total;
  final double subtotal;
  final double tax;
  final double discount;
  final double discountCapacity;
  final SalePreview? preview;
  final Failure? previewFailure;
  final bool isPreviewLoading;
  final bool canSubmitCheckout;
  final VoidCallback onRefreshPreview;
  final bool isSubmitting;
  final OnSubmitCheckout onSubmit;
  final SaleDetail? recordedSale;
  final VoidCallback? onViewRecordedSale;
  final VoidCallback? onViewRecordedReceipt;
  final VoidCallback onClearRecordedSale;

  @override
  Widget build(BuildContext context) {
    if (lines.isEmpty) {
      if (recordedSale != null) {
        return SingleChildScrollView(
          child: Padding(
            padding: const EdgeInsets.only(bottom: 8),
            child: _RecordedSaleSummaryCard(
              sale: recordedSale!,
              onViewDetail: onViewRecordedSale,
              onViewReceipt: onViewRecordedReceipt,
              onClear: onClearRecordedSale,
            ),
          ),
        );
      }

      return const Center(
        child: Padding(
          padding: EdgeInsets.all(24),
          child: Text('Cart is empty.'),
        ),
      );
    }

    return ListView.separated(
      padding: const EdgeInsets.fromLTRB(0, 8, 0, 8),
      itemCount: lines.length + 1,
      separatorBuilder: (_, _) => const SizedBox(height: 8),
      itemBuilder: (context, index) {
        if (index == lines.length) {
          return Padding(
            padding: const EdgeInsets.symmetric(horizontal: 8),
            child: _CheckoutSummaryCard(
              subtotal: subtotal,
              tax: tax,
              discount: discount,
              total: total,
              discountCapacity: discountCapacity,
              preview: preview,
              previewFailure: previewFailure,
              isPreviewLoading: isPreviewLoading,
              canSubmitCheckout: canSubmitCheckout,
              isSubmitting: isSubmitting,
              onRefreshPreview: onRefreshPreview,
              onSubmit: onSubmit,
            ),
          );
        }

        final line = lines[index];
        return Card(
          child: Padding(
            padding: const EdgeInsets.all(8),
            child: Column(
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        line.sellable.name,
                        key: Key('cart-line-name-${line.sellable.id}'),
                      ),
                    ),
                    IconButton(
                      key: Key('remove-from-cart-${line.sellable.id}'),
                      icon: const Icon(Icons.delete_outline),
                      onPressed: () => onRemove(line.sellable.id),
                    ),
                  ],
                ),
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    Text('Qty: ${_formatQuantity(line.quantity)}'),
                    Text('₹${line.lineTotal.toStringAsFixed(2)}'),
                  ],
                ),
                if (line.sellable.isService) ...[
                  const SizedBox(height: 8),
                  _ServicePriceField(
                    key: Key('service-unit-price-${line.sellable.id}'),
                    lineId: line.sellable.id,
                    price: line.effectiveUnitPrice,
                    onChanged: onUnitPriceChanged,
                  ),
                ],
                Row(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    IconButton(
                      key: Key('decrease-${line.sellable.id}'),
                      icon: const Icon(Icons.remove_circle_outline),
                      onPressed: () => onDecrease(line.sellable.id),
                    ),
                    IconButton(
                      key: Key('increase-${line.sellable.id}'),
                      icon: const Icon(Icons.add_circle_outline),
                      onPressed: () => onIncrease(line.sellable.id),
                    ),
                  ],
                ),
              ],
            ),
          ),
        );
      },
    );
  }

  String _formatQuantity(double quantity) {
    final whole = quantity.truncateToDouble();
    if (quantity == whole) {
      return whole.toInt().toString();
    }
    return quantity.toString();
  }
}

class _CheckoutSummaryCard extends StatelessWidget {
  const _CheckoutSummaryCard({
    required this.subtotal,
    required this.tax,
    required this.discount,
    required this.total,
    required this.discountCapacity,
    required this.preview,
    required this.previewFailure,
    required this.isPreviewLoading,
    required this.canSubmitCheckout,
    required this.isSubmitting,
    required this.onRefreshPreview,
    required this.onSubmit,
  });

  final double subtotal;
  final double tax;
  final double discount;
  final double total;
  final double discountCapacity;
  final SalePreview? preview;
  final Failure? previewFailure;
  final bool isPreviewLoading;
  final bool canSubmitCheckout;
  final bool isSubmitting;
  final VoidCallback onRefreshPreview;
  final OnSubmitCheckout onSubmit;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final previewRule = preview?.configuredSaleRule;

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'Checkout summary',
              style: theme.textTheme.titleMedium?.copyWith(
                fontWeight: FontWeight.w700,
              ),
            ),
            const SizedBox(height: 12),
            if (isPreviewLoading)
              Row(
                key: const Key('checkout-preview-loading'),
                children: [
                  SizedBox(
                    width: 16,
                    height: 16,
                    child: CircularProgressIndicator(
                      strokeWidth: 2,
                      color: theme.colorScheme.primary,
                    ),
                  ),
                  const SizedBox(width: 8),
                  const Expanded(child: Text('Refreshing server preview...')),
                ],
              )
            else if (previewFailure != null)
              Text(
                _failureMessage(previewFailure!),
                key: const Key('checkout-preview-error'),
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: theme.colorScheme.error,
                ),
              )
            else if (preview == null)
              Text(
                'Preview will load when the cart changes.',
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),
            const SizedBox(height: 8),
            _SummaryRow(label: 'Subtotal', value: _formatAmount(subtotal)),
            _SummaryRow(label: 'Tax', value: _formatAmount(tax)),
            _SummaryRow(label: 'Discount', value: _formatAmount(discount)),
            if (discountCapacity > 0)
              _SummaryRow(
                label: 'Discount capacity',
                value: _formatAmount(discountCapacity),
              ),
            const Divider(height: 20),
            _SummaryRow(
              label: 'Total',
              value: _formatAmount(total),
              emphasis: true,
            ),
            if (previewRule != null) ...[
              const SizedBox(height: 8),
              Text(
                'Configured rule: ${_configuredRuleLabel(previewRule)}',
                style: theme.textTheme.bodySmall?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),
            ],
            if (preview?.hasInfos == true) ...[
              const SizedBox(height: 12),
              Text(
                'Notes',
                style: theme.textTheme.titleSmall?.copyWith(
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 4),
              for (final info in preview!.infos)
                Padding(
                  padding: const EdgeInsets.only(bottom: 2),
                  child: Text('• ${info.message}'),
                ),
            ],
            if (preview?.hasWarnings == true) ...[
              const SizedBox(height: 12),
              Text(
                'Warnings',
                style: theme.textTheme.titleSmall?.copyWith(
                  fontWeight: FontWeight.w600,
                ),
              ),
              const SizedBox(height: 4),
              for (final warning in preview!.warnings)
                Padding(
                  padding: const EdgeInsets.only(bottom: 2),
                  child: Text('• ${warning.message}'),
                ),
            ],
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: OutlinedButton.icon(
                    key: const Key('refresh-preview-button'),
                    onPressed: isPreviewLoading ? null : onRefreshPreview,
                    icon: isPreviewLoading
                        ? const SizedBox(
                            width: 16,
                            height: 16,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.refresh_outlined),
                    label: Text(
                      previewFailure != null
                          ? 'Retry preview'
                          : 'Refresh preview',
                    ),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: FilledButton.icon(
                    key: const Key('checkout-button'),
                    onPressed: canSubmitCheckout && !isSubmitting
                        ? () {
                            unawaited(onSubmit());
                          }
                        : null,
                    icon: const Icon(Icons.shopping_cart_checkout_outlined),
                    label: Text(
                      canSubmitCheckout ? 'Checkout' : 'Preview required',
                    ),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _RecordedSaleSummaryCard extends StatelessWidget {
  const _RecordedSaleSummaryCard({
    required this.sale,
    required this.onClear,
    this.onViewDetail,
    this.onViewReceipt,
  });

  final SaleDetail sale;
  final VoidCallback onClear;
  final VoidCallback? onViewDetail;
  final VoidCallback? onViewReceipt;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(8, 8, 8, 8),
      child: Card(
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                'Invoice recorded',
                style: Theme.of(context).textTheme.titleMedium?.copyWith(
                  fontWeight: FontWeight.w700,
                ),
              ),
              const SizedBox(height: 8),
              Text('Invoice: ${sale.invoiceNumber}'),
              const SizedBox(height: 6),
              Text('Customer: ${sale.customerName ?? 'Walk-in customer'}'),
              if ((sale.customerPhone?.isNotEmpty ?? false))
                Text('Phone: ${sale.customerPhone}'),
              const SizedBox(height: 10),
              _SummaryRow(
                label: 'Subtotal',
                value: _formatAmount(sale.totalBeforeDiscount),
              ),
              _SummaryRow(
                label: 'Discount',
                value: _formatAmount(sale.totalDiscountAmount),
              ),
              _SummaryRow(
                label: 'Tax',
                value: _formatAmount(sale.totalTaxAmount),
              ),
              const Divider(height: 20),
              _SummaryRow(
                label: 'Total',
                value: _formatAmount(sale.totalAmount),
                emphasis: true,
              ),
              if (sale.dueAmount > 0)
                _SummaryRow(label: 'Due', value: _formatAmount(sale.dueAmount)),
              const SizedBox(height: 12),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  TextButton.icon(
                    key: const Key('recorded-sale-detail-button'),
                    onPressed: onViewDetail,
                    icon: const Icon(Icons.visibility),
                    label: const Text('View detail'),
                  ),
                  TextButton.icon(
                    key: const Key('recorded-sale-receipt-button'),
                    onPressed: onViewReceipt,
                    icon: const Icon(Icons.receipt_long),
                    label: const Text('View receipt'),
                  ),
                ],
              ),
              const SizedBox(height: 10),
              FilledButton.icon(
                key: const Key('recorded-sale-clear-button'),
                onPressed: onClear,
                icon: const Icon(Icons.add_shopping_cart_outlined),
                label: const Text('Start new sale'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _SummaryRow extends StatelessWidget {
  const _SummaryRow({
    required this.label,
    required this.value,
    this.emphasis = false,
  });

  final String label;
  final String value;
  final bool emphasis;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final style = emphasis
        ? theme.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w700)
        : theme.textTheme.bodyMedium;

    return Padding(
      padding: const EdgeInsets.only(bottom: 4),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Text(label, style: style),
          Text(value, style: style),
        ],
      ),
    );
  }
}

class _ServicePriceField extends StatefulWidget {
  const _ServicePriceField({
    super.key,
    required this.lineId,
    required this.price,
    required this.onChanged,
  });

  final String lineId;
  final double price;
  final OnUnitPriceChanged onChanged;

  @override
  State<_ServicePriceField> createState() => _ServicePriceFieldState();
}

class _ServicePriceFieldState extends State<_ServicePriceField> {
  late TextEditingController _controller;

  @override
  void initState() {
    super.initState();
    _controller = TextEditingController(
      text: widget.price.toStringAsFixed(2),
    );
  }

  @override
  void didUpdateWidget(_ServicePriceField oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.price != widget.price) {
      _controller.text = widget.price.toStringAsFixed(2);
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return TextField(
      controller: _controller,
      keyboardType: TextInputType.number,
      decoration: const InputDecoration(
        labelText: 'Unit price',
        border: OutlineInputBorder(),
      ),
      onChanged: (value) {
        final parsed = double.tryParse(value.trim());
        if (parsed != null) {
          widget.onChanged(widget.lineId, parsed);
        }
      },
    );
  }
}

String _configuredRuleLabel(SalePreviewConfiguredSaleRule rule) {
  final threshold = rule.thresholdAmount == null
      ? ''
      : ' from ₹${rule.thresholdAmount!.toStringAsFixed(2)}';
  return '${rule.ruleType} • ${rule.percentage.toStringAsFixed(0)}%$threshold';
}

String _formatAmount(num amount) {
  return '₹${amount.toStringAsFixed(2)}';
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
