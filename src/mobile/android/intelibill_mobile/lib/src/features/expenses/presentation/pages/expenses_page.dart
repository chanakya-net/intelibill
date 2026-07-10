import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
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
    final l10n = AppLocalizations.of(context)!;
    if (state.isLoading && state.expenses.isEmpty) {
      return const Center(child: CircularProgressIndicator());
    }
    if (state.listFailure != null && state.expenses.isEmpty) {
      return _ExpensesFailureState(
        details: _localizeFailure(l10n, state.listFailure!),
        onRetry: () => unawaited(
          ref.read(expensesControllerProvider.notifier).refresh(),
        ),
      );
    }

    return RefreshIndicator(
      onRefresh: () => ref.read(expensesControllerProvider.notifier).refresh(),
      child: state.expenses.isEmpty
          ? ListView(
              physics: const AlwaysScrollableScrollPhysics(),
              children: [
                Padding(
                  padding: const EdgeInsets.all(32),
                  child: Center(child: Text(l10n.expensesNoExpensesFound)),
                ),
              ],
            )
          : ListView(
              physics: const AlwaysScrollableScrollPhysics(),
              children: [
                if (state.isRefreshing)
                  const LinearProgressIndicator(
                    key: Key('expenses-refresh-progress'),
                  ),
                if (state.listFailure != null)
                  _ExpensesRefreshFailure(
                    details: _localizeFailure(l10n, state.listFailure!),
                    onRetry: () => unawaited(
                      ref.read(expensesControllerProvider.notifier).refresh(),
                    ),
                  ),
                for (final expense in state.expenses)
                  ExpenseCard(expense: expense),
              ],
            ),
    );
  }
}

class _ExpensesRefreshFailure extends StatelessWidget {
  const _ExpensesRefreshFailure({
    required this.details,
    required this.onRetry,
  });

  final String details;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    return Card(
      margin: const EdgeInsets.all(12),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Row(
          children: [
            const Icon(Icons.error_outline),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(l10n.expensesRefreshFailed),
                  Text(details),
                ],
              ),
            ),
            TextButton(onPressed: onRetry, child: Text(l10n.expensesRetry)),
          ],
        ),
      ),
    );
  }
}

class _ExpensesFailureState extends StatelessWidget {
  const _ExpensesFailureState({
    required this.details,
    required this.onRetry,
  });

  final String details;
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
            const Icon(Icons.error_outline, size: 48),
            const SizedBox(height: 12),
            Text(
              l10n.expensesUnableToLoad,
              style: Theme.of(context).textTheme.titleMedium,
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 8),
            Text(details, textAlign: TextAlign.center),
            const SizedBox(height: 16),
            FilledButton(
              onPressed: onRetry,
              child: Text(l10n.expensesRetry),
            ),
          ],
        ),
      ),
    );
  }
}

String _localizeFailure(AppLocalizations l10n, Failure failure) {
  return failure.when(
    validation: (message, _) => message ?? l10n.expensesErrorGeneric,
    unauthorized: (_) => l10n.expensesErrorUnauthorized,
    forbidden: (_) => l10n.expensesErrorForbidden,
    notFound: (_) => l10n.expensesErrorGeneric,
    server: (message, _) => message ?? l10n.expensesErrorGeneric,
    network: (_) => l10n.expensesErrorNetwork,
    timeout: (_) => l10n.expensesErrorTimeout,
    serialization: (_) => l10n.expensesErrorGeneric,
    unknown: (_) => l10n.expensesErrorGeneric,
  );
}
