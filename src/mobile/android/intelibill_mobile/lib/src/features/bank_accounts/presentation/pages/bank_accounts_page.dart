import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/controllers/bank_accounts_controller.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/widgets/bank_account_card.dart';

class BankAccountsPage extends ConsumerWidget {
  const BankAccountsPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(bankAccountsControllerProvider);
    final l10n = AppLocalizations.of(context)!;
    return Scaffold(
      appBar: AppBar(title: Text(l10n.bankAccountsTitle)),
      body: _buildBody(context, ref, state, l10n),
    );
  }

  Widget _buildBody(
    BuildContext context,
    WidgetRef ref,
    BankAccountsState state,
    AppLocalizations l10n,
  ) {
    if (state.isLoading && state.accounts.isEmpty) {
      return const Center(child: CircularProgressIndicator());
    }
    if (state.failure != null && state.accounts.isEmpty) {
      return _FailureView(
        message: _failureMessage(l10n, state.failure!),
        onRetry: () =>
            ref.read(bankAccountsControllerProvider.notifier).retry(),
        retryLabel: l10n.bankAccountsRetry,
      );
    }
    if (state.accounts.isEmpty) {
      return Center(child: Text(l10n.bankAccountsEmpty));
    }
    return RefreshIndicator(
      onRefresh: () =>
          ref.read(bankAccountsControllerProvider.notifier).retry(),
      child: ListView.builder(
        physics: const AlwaysScrollableScrollPhysics(),
        itemCount: state.accounts.length,
        itemBuilder: (context, index) => BankAccountCard(
          account: state.accounts[index],
        ),
      ),
    );
  }

  String _failureMessage(AppLocalizations l10n, Failure failure) {
    return failure.when(
      validation: (_, _) => l10n.bankAccountsUnableToLoad,
      unauthorized: (_) => l10n.bankAccountsErrorUnauthorized,
      forbidden: (_) => l10n.bankAccountsErrorForbidden,
      notFound: (_) => l10n.bankAccountsUnableToLoad,
      server: (_, _) => l10n.bankAccountsUnableToLoad,
      network: (_) => l10n.bankAccountsErrorNetwork,
      timeout: (_) => l10n.bankAccountsErrorTimeout,
      serialization: (_) => l10n.bankAccountsUnableToLoad,
      unknown: (_) => l10n.bankAccountsUnableToLoad,
    );
  }
}

class _FailureView extends StatelessWidget {
  const _FailureView({
    required this.message,
    required this.onRetry,
    required this.retryLabel,
  });

  final String message;
  final VoidCallback onRetry;
  final String retryLabel;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(message),
          const SizedBox(height: 12),
          FilledButton(onPressed: onRetry, child: Text(retryLabel)),
        ],
      ),
    );
  }
}
