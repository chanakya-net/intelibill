import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/controllers/expenses_controller.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/widgets/expense_card.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/widgets/expense_detail_sheet.dart';

class ExpensesPage extends ConsumerWidget {
  const ExpensesPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(expensesControllerProvider);
    final l10n = AppLocalizations.of(context)!;

    return Scaffold(
      appBar: AppBar(title: Text(l10n.shellManageExpenses)),
      body: Column(
        children: [
          _ExpenseStatusFilters(
            selectedFilter: state.statusFilter,
            onFilterChanged: (filter) {
              ref
                  .read(expensesControllerProvider.notifier)
                  .updateStatusFilter(filter);
            },
          ),
          Expanded(child: _ExpensesBody(state: state)),
        ],
      ),
    );
  }
}

class _ExpenseStatusFilters extends StatelessWidget {
  const _ExpenseStatusFilters({
    required this.selectedFilter,
    required this.onFilterChanged,
  });

  final ExpenseStatusFilter selectedFilter;
  final ValueChanged<ExpenseStatusFilter> onFilterChanged;

  @override
  Widget build(BuildContext context) {
    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
      child: Row(
        children: [
          _buildChip('All', ExpenseStatusFilter.all),
          const SizedBox(width: 8),
          _buildChip('Active', ExpenseStatusFilter.active),
          const SizedBox(width: 8),
          _buildChip('Voided', ExpenseStatusFilter.voided),
        ],
      ),
    );
  }

  Widget _buildChip(String label, ExpenseStatusFilter filter) {
    return FilterChip(
      label: Text(label),
      selected: selectedFilter == filter,
      onSelected: (_) => onFilterChanged(filter),
    );
  }
}

class _ExpensesBody extends ConsumerWidget {
  const _ExpensesBody({required this.state});

  final ExpensesState state;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    if (state.isLoading && state.page == null) {
      return const Center(child: CircularProgressIndicator());
    }

    final expenses = state.filteredExpenses;
    final hasLoadedExpenses = state.page?.items.isNotEmpty ?? false;
    final hasFailure = state.failure != null;
    final hasData = state.page != null;

    if (hasFailure && !hasData) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Text('Unable to load expenses'),
            const SizedBox(height: 12),
            FilledButton(
              onPressed: () => unawaited(
                ref.read(expensesControllerProvider.notifier).refresh(),
              ),
              child: Text(AppLocalizations.of(context)!.customersRetry),
            ),
          ],
        ),
      );
    }

    return Stack(
      children: [
        RefreshIndicator(
          onRefresh: ref.read(expensesControllerProvider.notifier).refresh,
          child: expenses.isEmpty
              ? ListView(
                  physics: const AlwaysScrollableScrollPhysics(),
                  children: [
                    const SizedBox(height: 160),
                    Center(
                      child: Text(
                        hasLoadedExpenses
                            ? 'No expenses match the selected filter'
                            : 'No expenses found',
                      ),
                    ),
                  ],
                )
              : ListView.builder(
                  physics: const AlwaysScrollableScrollPhysics(),
                  padding: const EdgeInsets.symmetric(vertical: 8),
                  itemCount: expenses.length,
                  itemBuilder: (context, index) => ExpenseCard(
                    expense: expenses[index],
                    onTap: () => unawaited(
                      _openExpenseDetail(context, ref, expenses[index].id),
                    ),
                  ),
                ),
        ),
        if (hasFailure)
          Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Text('Unable to load expenses'),
                const SizedBox(height: 12),
                FilledButton(
                  onPressed: () => unawaited(
                    ref.read(expensesControllerProvider.notifier).refresh(),
                  ),
                  child: Text(AppLocalizations.of(context)!.customersRetry),
                ),
              ],
            ),
          ),
      ],
    );
  }
}

Future<void> _openExpenseDetail(
  BuildContext context,
  WidgetRef ref,
  String id,
) async {
  final controller = ref.read(expensesControllerProvider.notifier);
  unawaited(controller.openExpense(id));
  await showExpenseDetailSheet(context);
  if (!context.mounted) return;
  controller.clearSelectedExpense();
}
