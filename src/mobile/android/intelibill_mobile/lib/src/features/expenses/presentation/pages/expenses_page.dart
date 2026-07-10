import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/controllers/expenses_controller.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/widgets/expense_card.dart';

class ExpensesPage extends ConsumerWidget {
  const ExpensesPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(expensesControllerProvider);
    return Scaffold(
      appBar: AppBar(
        title: Text(AppLocalizations.of(context)!.shellManageExpenses),
      ),
      body: _ExpensesBody(state: state),
    );
  }
}

class _ExpensesBody extends ConsumerWidget {
  const _ExpensesBody({required this.state});

  final ExpensesState state;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    if (state.isLoading) {
      return const Center(child: CircularProgressIndicator());
    }
    if (state.failure != null) {
      return Center(
        child: FilledButton(
          onPressed: () => unawaited(
            ref.read(expensesControllerProvider.notifier).refresh(),
          ),
          child: const Text('Retry'),
        ),
      );
    }

    return RefreshIndicator(
      onRefresh: () => ref.read(expensesControllerProvider.notifier).refresh(),
      child: state.expenses.isEmpty
          ? ListView(
              physics: const AlwaysScrollableScrollPhysics(),
              children: const [
                Padding(
                  padding: EdgeInsets.all(32),
                  child: Center(child: Text('No expenses found')),
                ),
              ],
            )
          : ListView.builder(
              physics: const AlwaysScrollableScrollPhysics(),
              itemCount: state.expenses.length,
              itemBuilder: (context, index) {
                return ExpenseCard(expense: state.expenses[index]);
              },
            ),
    );
  }
}
