import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note.dart';
import 'package:intelibill_mobile/src/features/credit_notes/presentation/controllers/credit_notes_controller.dart';

class CreditNotesPage extends ConsumerStatefulWidget {
  const CreditNotesPage({super.key});

  @override
  ConsumerState<CreditNotesPage> createState() => _CreditNotesPageState();
}

class _CreditNotesPageState extends ConsumerState<CreditNotesPage> {
  late final TextEditingController _searchController;
  late final TextEditingController _verifyController;
  Timer? _searchDebounce;

  @override
  void initState() {
    super.initState();
    _searchController = TextEditingController();
    _verifyController = TextEditingController();
  }

  @override
  void dispose() {
    _searchController.dispose();
    _verifyController.dispose();
    _searchDebounce?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(creditNotesControllerProvider);
    final l10n = AppLocalizations.of(context)!;

    ref.listen(creditNotesControllerProvider.select((s) => s.selectedNote), (
      previous,
      next,
    ) {
      if (next != null && next != previous) {
        unawaited(_showDetailSheet(context, next));
      }
    });

    return Scaffold(
      appBar: AppBar(
        title: Text(l10n.shellManageCreditNotes),
        actions: [
          IconButton(
            tooltip: l10n.shellProfile,
            icon: const Icon(Icons.account_circle),
            onPressed: () => context.push(AppRoutes.profile),
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () =>
            ref.read(creditNotesControllerProvider.notifier).refresh(),
        child: ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: const EdgeInsets.all(16),
          children: [
            Text(
              l10n.shellManageCreditNotes,
              style: Theme.of(context).textTheme.headlineSmall,
            ),
            const SizedBox(height: 12),
            TextField(
              key: const Key('credit-notes-search'),
              controller: _searchController,
              decoration: InputDecoration(
                labelText: l10n.commonSearch,
                prefixIcon: const Icon(Icons.search),
                border: const OutlineInputBorder(),
              ),
              onChanged: (value) {
                _searchDebounce?.cancel();
                _searchDebounce = Timer(
                  const Duration(milliseconds: 350),
                  () {
                    if (mounted) {
                      unawaited(
                        ref
                            .read(creditNotesControllerProvider.notifier)
                            .updateSearch(value),
                      );
                    }
                  },
                );
              },
            ),
            const SizedBox(height: 12),
            TextField(
              key: const Key('credit-notes-verify'),
              controller: _verifyController,
              decoration: InputDecoration(
                labelText: l10n.creditNotesVerifyCode,
                prefixIcon: const Icon(Icons.qr_code),
                suffixIcon: IconButton(
                  icon: const Icon(Icons.check),
                  onPressed: () {
                    unawaited(
                      ref
                          .read(creditNotesControllerProvider.notifier)
                          .verifyCode(_verifyController.text),
                    );
                  },
                ),
                border: const OutlineInputBorder(),
              ),
              onSubmitted: (value) {
                unawaited(
                  ref
                      .read(creditNotesControllerProvider.notifier)
                      .verifyCode(
                        value,
                      ),
                );
              },
            ),
            const SizedBox(height: 12),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                FilterChip(
                  label: Text(l10n.creditNotesFilterAll),
                  selected: state.statusFilter == null,
                  onSelected: (_) {
                    _searchDebounce?.cancel();
                    _searchController.clear();
                    unawaited(
                      ref
                          .read(creditNotesControllerProvider.notifier)
                          .clearFilters(),
                    );
                  },
                ),
                for (final option in _statusOptions)
                  FilterChip(
                    label: Text(option),
                    selected: state.statusFilter == option,
                    onSelected: (_) => unawaited(
                      ref
                          .read(creditNotesControllerProvider.notifier)
                          .updateStatusFilter(option),
                    ),
                  ),
              ],
            ),
            const SizedBox(height: 16),
            _buildBody(context, state, l10n),
          ],
        ),
      ),
    );
  }

  Widget _buildBody(
    BuildContext context,
    CreditNotesState state,
    AppLocalizations l10n,
  ) {
    if (state.isLoading && state.notes.isEmpty) {
      return const Center(
        child: Padding(
          padding: EdgeInsets.all(48),
          child: CircularProgressIndicator(),
        ),
      );
    }

    if (state.failure != null && state.notes.isEmpty) {
      return _ErrorState(
        message: _messageForFailure(l10n, state.failure!),
        onRetry: () => unawaited(
          ref.read(creditNotesControllerProvider.notifier).refresh(),
        ),
      );
    }

    if (state.notes.isEmpty) {
      return const _EmptyState();
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (final note in state.notes)
          Padding(
            padding: const EdgeInsets.only(bottom: 12),
            child: _CreditNoteCard(
              note: note,
              onTap: () => unawaited(
                ref
                    .read(creditNotesControllerProvider.notifier)
                    .openByCode(note.code),
              ),
            ),
          ),
        if (state.isLoadingMore)
          const Padding(
            padding: EdgeInsets.symmetric(vertical: 16),
            child: Center(child: CircularProgressIndicator()),
          ),
        if (state.hasMore && !state.isLoadingMore)
          Center(
            child: TextButton(
              onPressed: () => unawaited(
                ref.read(creditNotesControllerProvider.notifier).loadMore(),
              ),
              child: Text(l10n.creditNotesLoadMore),
            ),
          ),
      ],
    );
  }

  Future<void> _showDetailSheet(BuildContext context, CreditNote note) async {
    await showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      isScrollControlled: true,
      builder: (context) {
        return _CreditNoteDetailSheet(note: note);
      },
    );
    if (!mounted) return;
    ref.read(creditNotesControllerProvider.notifier).selectNote(null);
  }

  String _messageForFailure(AppLocalizations l10n, Failure failure) {
    switch (failure) {
      case NetworkFailure():
        return l10n.customersErrorNetwork;
      case TimeoutFailure():
        return l10n.customersErrorTimeout;
      case UnauthorizedFailure():
        return l10n.customersErrorUnauthorized;
      case ForbiddenFailure():
        return l10n.customersErrorForbidden;
      default:
        return l10n.customersErrorGeneric;
    }
  }
}

const List<String> _statusOptions = [
  'active',
  'voided',
  'expired',
  'fullyRedeemed',
];

class _CreditNoteCard extends StatelessWidget {
  const _CreditNoteCard({required this.note, required this.onTap});

  final CreditNote note;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final expiry = note.expiresAt == null
        ? l10n.creditNotesNoExpiry
        : DateFormat('dd MMM yyyy').format(note.expiresAt!);
    return Card(
      child: ListTile(
        onTap: onTap,
        title: Text(note.code),
        subtitle: Text(
          '${note.customerName ?? "Walk-in"} • ${note.invoiceNumber}\n$expiry',
        ),
        isThreeLine: true,
        trailing: Text(note.status),
      ),
    );
  }
}

class _CreditNoteDetailSheet extends StatelessWidget {
  const _CreditNoteDetailSheet({required this.note});

  final CreditNote note;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 0, 16, 24),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(note.code, style: Theme.of(context).textTheme.titleLarge),
          const SizedBox(height: 8),
          Text(note.reason),
          const SizedBox(height: 12),
          Text('${l10n.creditNotesInvoiceLabel} ${note.invoiceNumber}'),
          Text('${l10n.creditNotesReturnLabel} ${note.returnNumber}'),
          Text(
            '${l10n.creditNotesBalanceLabel} ${note.availableBalance.toStringAsFixed(2)}',
          ),
          const SizedBox(height: 12),
          Align(
            alignment: Alignment.centerRight,
            child: TextButton(
              onPressed: () => Navigator.of(context).pop(),
              child: Text(l10n.creditNotesClose),
            ),
          ),
        ],
      ),
    );
  }
}

class _ErrorState extends StatelessWidget {
  const _ErrorState({required this.message, required this.onRetry});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(Icons.error_outline, size: 48),
            const SizedBox(height: 12),
            Text(message, textAlign: TextAlign.center),
            const SizedBox(height: 12),
            FilledButton(
              onPressed: onRetry,
              child: Text(AppLocalizations.of(context)!.creditNotesRetry),
            ),
          ],
        ),
      ),
    );
  }
}

class _EmptyState extends StatelessWidget {
  const _EmptyState();

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(48),
        child: Text(l10n.creditNotesEmpty),
      ),
    );
  }
}
