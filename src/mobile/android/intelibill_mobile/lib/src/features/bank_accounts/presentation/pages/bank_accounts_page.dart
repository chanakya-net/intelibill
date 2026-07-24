import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/app/shell/menu_visibility.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/domain/entities/auth_session.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/domain/entities/bank_account.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/controllers/bank_accounts_controller.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/widgets/bank_account_card.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/widgets/create_bank_account_sheet.dart';
import 'package:intelibill_mobile/src/features/bank_accounts/presentation/widgets/edit_bank_account_sheet.dart';

class BankAccountsPage extends ConsumerStatefulWidget {
  const BankAccountsPage({super.key});

  static const addBankAccountFabKey = Key('bank-accounts-add-fab');
  static const searchFieldKey = Key('bank-accounts-search');
  static const deleteConfirmButtonKey = Key('bank-accounts-delete-confirm');

  @override
  ConsumerState<BankAccountsPage> createState() => _BankAccountsPageState();
}

class _BankAccountsPageState extends ConsumerState<BankAccountsPage> {
  late final TextEditingController _searchController;

  @override
  void initState() {
    super.initState();
    _searchController = TextEditingController();
  }

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  Future<void> _openCreateSheet() async {
    final created = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      showDragHandle: true,
      builder: (context) => const CreateBankAccountSheet(),
    );
    if (!mounted || created != true) return;
    final l10n = AppLocalizations.of(context)!;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(l10n.bankAccountsCreateSuccess)),
    );
  }

  Future<void> _openEditSheet(BankAccount account) async {
    final updated = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      showDragHandle: true,
      builder: (context) => EditBankAccountSheet(account: account),
    );
    if (!mounted || updated != true) return;
    final l10n = AppLocalizations.of(context)!;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(l10n.bankAccountsUpdateSuccess)),
    );
  }

  Future<void> _openDeleteDialog(BankAccount account) async {
    final deleted = await showDialog<bool>(
      context: context,
      builder: (_) => _DeleteBankAccountDialog(account: account),
    );
    if (!mounted || deleted != true) return;
    final l10n = AppLocalizations.of(context)!;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(l10n.bankAccountsDeleteSuccess)),
    );
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(bankAccountsControllerProvider);
    final session = ref.watch(authControllerProvider).value?.session;
    final l10n = AppLocalizations.of(context)!;
    return Scaffold(
      appBar: AppBar(title: Text(l10n.bankAccountsTitle)),
      floatingActionButton: canManageBankAccounts(session)
          ? FloatingActionButton.extended(
              key: BankAccountsPage.addBankAccountFabKey,
              onPressed: () => unawaited(_openCreateSheet()),
              tooltip: l10n.bankAccountsAdd,
              icon: const Icon(Icons.add),
              label: Text(l10n.bankAccountsAdd),
            )
          : null,
      body: Column(
        children: [
          _buildSearchField(l10n),
          Expanded(child: _buildBody(context, ref, state, l10n, session)),
        ],
      ),
    );
  }

  Widget _buildSearchField(AppLocalizations l10n) {
    final hasQuery = ref.watch(
      bankAccountsControllerProvider.select(
        (state) => state.searchQuery.isNotEmpty,
      ),
    );
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
      child: TextField(
        key: BankAccountsPage.searchFieldKey,
        controller: _searchController,
        decoration: InputDecoration(
          hintText: l10n.bankAccountsSearchHint,
          prefixIcon: const Icon(Icons.search),
          suffixIcon: hasQuery
              ? IconButton(
                  tooltip: l10n.bankAccountsClearSearch,
                  icon: const Icon(Icons.clear),
                  onPressed: () {
                    _searchController.clear();
                    ref
                        .read(bankAccountsControllerProvider.notifier)
                        .updateSearch('');
                  },
                )
              : null,
          border: const OutlineInputBorder(),
          isDense: true,
        ),
        onChanged: (query) {
          ref.read(bankAccountsControllerProvider.notifier).updateSearch(query);
        },
      ),
    );
  }

  Widget _buildBody(
    BuildContext context,
    WidgetRef ref,
    BankAccountsState state,
    AppLocalizations l10n,
    AuthSession? session,
  ) {
    if (state.isLoading && state.accounts.isEmpty) {
      return const Center(child: CircularProgressIndicator());
    }
    if (state.failure != null && state.accounts.isEmpty) {
      return _FailureView(
        title: l10n.bankAccountsUnableToLoad,
        message: _failureMessage(l10n, state.failure!),
        onRetry: () =>
            ref.read(bankAccountsControllerProvider.notifier).retry(),
        retryLabel: l10n.bankAccountsRetry,
      );
    }

    final accounts = state.filteredAccounts;
    final emptyMessage = state.accounts.isEmpty
        ? l10n.bankAccountsEmpty
        : l10n.bankAccountsNoResults;
    final canManage = canManageBankAccounts(session);

    return RefreshIndicator(
      semanticsLabel: l10n.bankAccountsRefresh,
      onRefresh: () =>
          ref.read(bankAccountsControllerProvider.notifier).refresh(),
      child: accounts.isEmpty
          ? ListView(
              physics: const AlwaysScrollableScrollPhysics(),
              children: [
                Padding(
                  padding: const EdgeInsets.all(32),
                  child: Column(
                    children: [
                      Icon(
                        Icons.account_balance_outlined,
                        size: 56,
                        color: Theme.of(context).colorScheme.primary.withValues(
                          alpha: 0.7,
                        ),
                      ),
                      const SizedBox(height: 16),
                      Text(
                        emptyMessage,
                        style: Theme.of(context).textTheme.titleMedium,
                        textAlign: TextAlign.center,
                      ),
                    ],
                  ),
                ),
              ],
            )
          : ListView.builder(
              physics: const AlwaysScrollableScrollPhysics(),
              padding: const EdgeInsets.only(bottom: 88),
              itemCount: accounts.length,
              itemBuilder: (context, index) => BankAccountCard(
                account: accounts[index],
                onEdit: canManage
                    ? () => unawaited(_openEditSheet(accounts[index]))
                    : null,
                onDelete: canManage
                    ? () => unawaited(_openDeleteDialog(accounts[index]))
                    : null,
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

class _DeleteBankAccountDialog extends ConsumerWidget {
  const _DeleteBankAccountDialog({required this.account});

  final BankAccount account;

  Future<void> _delete(BuildContext context, WidgetRef ref) async {
    final deleted = await ref
        .read(bankAccountsControllerProvider.notifier)
        .deleteBankAccount(account.id);
    if (context.mounted && deleted) Navigator.of(context).pop(true);
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(bankAccountsControllerProvider);
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    final disabled = state.isSubmitting;
    return AlertDialog(
      title: Text(l10n.bankAccountsDeleteTitle),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(l10n.bankAccountsDeleteConfirmation(account.bankName)),
          if (state.submitFailure != null) ...[
            const SizedBox(height: 12),
            Container(
              width: double.infinity,
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(
                borderRadius: BorderRadius.circular(12),
                color: theme.colorScheme.errorContainer,
              ),
              child: Text(
                l10n.bankAccountsDeleteError,
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: theme.colorScheme.onErrorContainer,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
          ],
        ],
      ),
      actions: [
        TextButton(
          onPressed: disabled ? null : () => Navigator.of(context).pop(false),
          child: Text(l10n.bankAccountsDeleteCancel),
        ),
        FilledButton(
          key: BankAccountsPage.deleteConfirmButtonKey,
          style: FilledButton.styleFrom(
            backgroundColor: theme.colorScheme.error,
            foregroundColor: theme.colorScheme.onError,
          ),
          onPressed: disabled ? null : () => unawaited(_delete(context, ref)),
          child: Text(l10n.bankAccountsDeleteConfirm),
        ),
      ],
    );
  }
}

class _FailureView extends StatelessWidget {
  const _FailureView({
    required this.title,
    required this.message,
    required this.onRetry,
    required this.retryLabel,
  });

  final String title;
  final String message;
  final VoidCallback onRetry;
  final String retryLabel;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              Icons.cloud_off_outlined,
              size: 48,
              color: theme.colorScheme.onSurfaceVariant,
            ),
            const SizedBox(height: 16),
            Text(
              title,
              style: const TextStyle(
                fontSize: 18,
                fontWeight: FontWeight.w600,
              ),
              textAlign: TextAlign.center,
            ),
            if (message != title) ...[
              const SizedBox(height: 8),
              Text(message, textAlign: TextAlign.center),
            ],
            const SizedBox(height: 16),
            FilledButton(onPressed: onRetry, child: Text(retryLabel)),
          ],
        ),
      ),
    );
  }
}
