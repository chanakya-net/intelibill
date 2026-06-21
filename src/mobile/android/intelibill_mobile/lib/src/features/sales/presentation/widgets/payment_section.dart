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

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Text(
          'Payment',
          style: TextStyle(fontSize: 16, fontWeight: FontWeight.w600),
        ),
        const SizedBox(height: 8),
        Wrap(
          spacing: 8,
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
        const SizedBox(height: 8),
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
                  border: OutlineInputBorder(),
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
                  border: OutlineInputBorder(),
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
        const SizedBox(height: 8),
        Text(
          'Payable: ₹${state.payable.toStringAsFixed(2)}',
          style: theme.textTheme.bodyLarge,
        ),
      ],
    );
  }

  Widget _paymentMethodChip(
    PaymentMethod method,
    Key key, {
    required bool enabled,
    required ThemeData theme,
  }) {
    return ChoiceChip(
      key: key,
      label: Text(_paymentMethodLabel(method)),
      selected: state.paymentMethod == method,
      onSelected: enabled ? (_) => onPaymentMethodChanged(method) : null,
      selectedColor: theme.colorScheme.primaryContainer,
      labelStyle: TextStyle(
        color: enabled ? theme.colorScheme.onSurface : theme.disabledColor,
      ),
      side: BorderSide(
        color: enabled ? theme.colorScheme.outline : theme.disabledColor,
      ),
      showCheckmark: false,
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
