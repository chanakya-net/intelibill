import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/formatting/currency_formatter.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_list_item.dart';
import 'package:intl/intl.dart';

class ExpenseCard extends StatelessWidget {
  const ExpenseCard({required this.expense, required this.onTap, super.key});

  final ExpenseListItem expense;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final l10n = AppLocalizations.of(context)!;
    final status = expense.isVoided
        ? l10n.creditNotesReceiptStatusVoided
        : l10n.creditNotesReceiptStatusActive;

    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(12),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Flexible(
                    child: Text(
                      expense.categoryName,
                      style: theme.textTheme.titleMedium,
                    ),
                  ),
                  Flexible(
                    child: Text(
                      formatInr(expense.amount),
                      textAlign: TextAlign.end,
                      style: theme.textTheme.titleMedium?.copyWith(
                        fontWeight: FontWeight.bold,
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 8),
              Text(expense.paidTo),
              const SizedBox(height: 12),
              Wrap(
                alignment: WrapAlignment.spaceBetween,
                crossAxisAlignment: WrapCrossAlignment.center,
                spacing: 8,
                runSpacing: 8,
                children: [
                  Text(DateFormat('dd MMM yyyy').format(expense.expenseDate)),
                  Semantics(
                    label: l10n.expensesStatusSemantics(status),
                    child: ExcludeSemantics(
                      child: Chip(
                        label: Text(status),
                        visualDensity: VisualDensity.compact,
                        backgroundColor: expense.isVoided
                            ? theme.colorScheme.errorContainer
                            : theme.colorScheme.primaryContainer,
                      ),
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}
