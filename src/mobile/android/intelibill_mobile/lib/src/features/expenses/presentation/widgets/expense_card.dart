import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_list_item.dart';
import 'package:intl/intl.dart';

final NumberFormat _expenseAmountFormatter = NumberFormat.currency(
  locale: 'en_IN',
  symbol: '₹',
  decimalDigits: 2,
);
final DateFormat _expenseDateFormatter = DateFormat('MMM d, yyyy');

class ExpenseCard extends StatelessWidget {
  const ExpenseCard({required this.expense, super.key});

  final ExpenseListItem expense;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final statusColor = expense.isVoided
        ? theme.colorScheme.error
        : const Color(0xFF15803D);

    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    expense.categoryName,
                    style: theme.textTheme.titleMedium,
                  ),
                ),
                _StatusBadge(
                  label: expense.isVoided ? 'Voided' : 'Active',
                  color: statusColor,
                ),
              ],
            ),
            const SizedBox(height: 8),
            Text(expense.paidTo, style: theme.textTheme.bodyMedium),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: Text(
                    _expenseDateFormatter.format(expense.expenseDate),
                    style: theme.textTheme.bodySmall,
                  ),
                ),
                Text(
                  _expenseAmountFormatter.format(expense.amount),
                  style: theme.textTheme.titleSmall?.copyWith(
                    fontWeight: FontWeight.w700,
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

class _StatusBadge extends StatelessWidget {
  const _StatusBadge({required this.label, required this.color});

  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(12),
      ),
      child: Text(
        label,
        style: Theme.of(context).textTheme.labelMedium?.copyWith(
          color: color,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}
