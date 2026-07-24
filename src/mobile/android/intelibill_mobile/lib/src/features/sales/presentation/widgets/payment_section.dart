import 'package:flutter/material.dart';

import 'package:intelibill_mobile/src/features/sales/domain/entities/payment_method.dart';
import 'package:intelibill_mobile/src/features/sales/presentation/controllers/new_sale_controller.dart';

class PaymentSection extends StatelessWidget {
  const PaymentSection({
    super.key,
    required this.state,
    required this.paidAmountController,
    required this.dueAmountController,
    required this.paidAmountFocusNode,
    required this.dueAmountFocusNode,
    required this.onPaymentMethodChanged,
    required this.onPaidAmountChanged,
    required this.onDueAmountChanged,
    required this.onPaidAmountEditingComplete,
    required this.onDueAmountEditingComplete,
  });

  static const paidAmountFieldKey = Key('new-sale-paid-amount');
  static const dueAmountFieldKey = Key('new-sale-due-amount');
  static const paymentMethodCashKey = Key('new-sale-payment-method-cash');
  static const paymentMethodUpiKey = Key('new-sale-payment-method-upi');
  static const paymentMethodCardKey = Key('new-sale-payment-method-card');
  static const paymentMethodCreditKey = Key('new-sale-payment-method-credit');

  final NewSaleState state;
  final TextEditingController paidAmountController;
  final TextEditingController dueAmountController;
  final FocusNode paidAmountFocusNode;
  final FocusNode dueAmountFocusNode;
  final ValueChanged<PaymentMethod> onPaymentMethodChanged;
  final ValueChanged<double> onPaidAmountChanged;
  final ValueChanged<double> onDueAmountChanged;
  final VoidCallback onPaidAmountEditingComplete;
  final VoidCallback onDueAmountEditingComplete;

  @override
  Widget build(BuildContext context) {
    final hasCustomer = state.selectedCustomer != null;
    final theme = Theme.of(context);

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              'Payment',
              style: theme.textTheme.titleMedium?.copyWith(
                fontWeight: FontWeight.w700,
              ),
            ),
            const SizedBox(height: 12),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                _paymentMethodChip(
                  PaymentMethod.cash,
                  paymentMethodCashKey,
                  enabled: true,
                  theme: theme,
                ),
                _paymentMethodChip(
                  PaymentMethod.upi,
                  paymentMethodUpiKey,
                  enabled: true,
                  theme: theme,
                ),
                _paymentMethodChip(
                  PaymentMethod.card,
                  paymentMethodCardKey,
                  enabled: true,
                  theme: theme,
                ),
                _paymentMethodChip(
                  PaymentMethod.credit,
                  paymentMethodCreditKey,
                  enabled: hasCustomer,
                  theme: theme,
                ),
              ],
            ),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: TextField(
                    key: paidAmountFieldKey,
                    controller: paidAmountController,
                    focusNode: paidAmountFocusNode,
                    textInputAction: TextInputAction.next,
                    keyboardType: const TextInputType.numberWithOptions(
                      decimal: true,
                    ),
                    decoration: const InputDecoration(
                      labelText: 'Paid amount',
                      prefixText: '₹',
                    ),
                    onEditingComplete: onPaidAmountEditingComplete,
                    onChanged: (value) {
                      final parsed = double.tryParse(value.trim()) ?? 0;
                      onPaidAmountChanged(parsed);
                    },
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: TextField(
                    key: dueAmountFieldKey,
                    controller: dueAmountController,
                    focusNode: dueAmountFocusNode,
                    textInputAction: TextInputAction.done,
                    keyboardType: const TextInputType.numberWithOptions(
                      decimal: true,
                    ),
                    decoration: const InputDecoration(
                      labelText: 'Due amount',
                      prefixText: '₹',
                    ),
                    onEditingComplete: onDueAmountEditingComplete,
                    onChanged: (value) {
                      final parsed = double.tryParse(value.trim()) ?? 0;
                      onDueAmountChanged(parsed);
                    },
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12),
            Container(
              width: double.infinity,
              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
              decoration: BoxDecoration(
                color: const Color(0xFFFFEDD5),
                borderRadius: BorderRadius.circular(14),
              ),
              child: Text(
                'Payable: ₹${state.payable.toStringAsFixed(2)}',
                style: theme.textTheme.titleMedium?.copyWith(
                  fontWeight: FontWeight.w700,
                  color: const Color(0xFF7C2D12),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _paymentMethodChip(
    PaymentMethod method,
    Key key, {
    required bool enabled,
    required ThemeData theme,
  }) {
    final isSelected = state.paymentMethod == method;

    return ChoiceChip(
      key: key,
      label: Text(_paymentMethodLabel(method)),
      selected: isSelected,
      onSelected: enabled ? (_) => onPaymentMethodChanged(method) : null,
      selectedColor: const Color(0xFFFFEDD5),
      backgroundColor: theme.cardTheme.color,
      labelStyle: TextStyle(
        color: enabled
            ? (isSelected ? theme.colorScheme.primary : const Color(0xFF6B3A16))
            : theme.disabledColor,
        fontWeight: isSelected ? FontWeight.w700 : FontWeight.w500,
      ),
      side: BorderSide(
        color: enabled
            ? (isSelected ? theme.colorScheme.primary : const Color(0xFFFDBA74))
            : theme.disabledColor,
      ),
      showCheckmark: false,
      padding: const EdgeInsets.symmetric(horizontal: 4),
    );
  }

  String _paymentMethodLabel(PaymentMethod method) {
    switch (method) {
      case PaymentMethod.cash:
        return 'Cash';
      case PaymentMethod.upi:
        return 'UPI';
      case PaymentMethod.card:
        return 'Card';
      case PaymentMethod.credit:
        return 'Credit';
    }
  }
}
