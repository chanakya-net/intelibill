import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/app/shell/menu_visibility.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/auth/presentation/controllers/auth_controller.dart';
import 'package:intelibill_mobile/src/features/credit_notes/domain/entities/credit_note.dart';
import 'package:intelibill_mobile/src/features/credit_notes/presentation/controllers/credit_notes_controller.dart';
import 'package:intl/intl.dart';

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

  bool _onScroll(ScrollNotification notification) {
    if (notification is ScrollEndNotification &&
        notification.metrics.extentAfter == 0.0) {
      unawaited(
        ref.read(creditNotesControllerProvider.notifier).loadMore(),
      );
    }
    return false;
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(creditNotesControllerProvider);
    final l10n = AppLocalizations.of(context)!;
    final authState = ref.watch(authControllerProvider);
    final canVoidNotes = canManageCreditNotes(authState.value?.session);

    ref.listen(creditNotesControllerProvider.select((s) => s.selectedNote), (
      previous,
      next,
    ) {
      if (next != null && next != previous) {
        unawaited(_showDetailSheet(context, next, canVoidNotes));
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
      body: _buildBody(context, state, l10n),
    );
  }

  Widget _buildBody(
    BuildContext context,
    CreditNotesState state,
    AppLocalizations l10n,
  ) {
    if (state.isLoading && state.notes.isEmpty) {
      return const Center(child: CircularProgressIndicator());
    }

    if (state.failure != null && state.notes.isEmpty) {
      return _ErrorState(
        message: _localizeFailure(l10n, state.failure!),
        onRetry: () => unawaited(
          ref.read(creditNotesControllerProvider.notifier).refresh(),
        ),
      );
    }

    return NotificationListener<ScrollNotification>(
      onNotification: _onScroll,
      child: RefreshIndicator(
        onRefresh: () =>
            ref.read(creditNotesControllerProvider.notifier).refresh(),
        child: CustomScrollView(
          physics: const AlwaysScrollableScrollPhysics(),
          slivers: [
            SliverToBoxAdapter(
              child: Padding(
                padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    TextField(
                      key: const Key('credit-notes-search'),
                      controller: _searchController,
                      decoration: InputDecoration(
                        hintText: l10n.commonSearch,
                        prefixIcon: const Icon(Icons.search),
                        border: const OutlineInputBorder(),
                        isDense: true,
                      ),
                      onChanged: (value) {
                        _searchDebounce?.cancel();
                        _searchDebounce = Timer(
                          const Duration(milliseconds: 350),
                          () {
                            if (mounted) {
                              unawaited(
                                ref
                                    .read(
                                      creditNotesControllerProvider.notifier,
                                    )
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
                        hintText: l10n.creditNotesVerifyCode,
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
                        isDense: true,
                      ),
                      onSubmitted: (value) {
                        unawaited(
                          ref
                              .read(creditNotesControllerProvider.notifier)
                              .verifyCode(value),
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
                            label: Text(_statusLabel(l10n, option)),
                            selected: state.statusFilter == option,
                            onSelected: (_) => unawaited(
                              ref
                                  .read(creditNotesControllerProvider.notifier)
                                  .updateStatusFilter(option),
                            ),
                          ),
                      ],
                    ),
                    const SizedBox(height: 12),
                  ],
                ),
              ),
            ),
            if (state.notes.isEmpty)
              SliverFillRemaining(
                hasScrollBody: false,
                child: _EmptyState(message: l10n.creditNotesEmpty),
              )
            else
              SliverList(
                delegate: SliverChildBuilderDelegate(
                  (context, index) {
                    if (index >= state.notes.length) {
                      return const Padding(
                        padding: EdgeInsets.all(16),
                        child: Center(child: CircularProgressIndicator()),
                      );
                    }

                    final note = state.notes[index];
                    return _CreditNoteCard(
                      note: note,
                      onTap: () => unawaited(
                        ref
                            .read(creditNotesControllerProvider.notifier)
                            .openByCode(note.code),
                      ),
                    );
                  },
                  childCount:
                      state.notes.length + (state.isLoadingMore ? 1 : 0),
                ),
              ),
          ],
        ),
      ),
    );
  }

  Future<void> _showDetailSheet(
    BuildContext context,
    CreditNote note,
    bool canVoid,
  ) async {
    final action = await showModalBottomSheet<_CreditNoteDetailSheetAction>(
      context: context,
      showDragHandle: true,
      isScrollControlled: true,
      builder: (context) {
        return _CreditNoteDetailSheet(
          note: note,
          canVoid: canVoid,
          onOpenReceipt: () {
            Navigator.of(context).pop(_CreditNoteDetailSheetAction.openReceipt);
          },
          onVoid: (reason) {
            return ref
                .read(creditNotesControllerProvider.notifier)
                .voidActiveNote(code: note.code, reason: reason)
                .then(
                  (isSuccess) => isSuccess,
                );
          },
        );
      },
    );
    if (!mounted) return;
    if (action == _CreditNoteDetailSheetAction.voided) return;
    if (action == _CreditNoteDetailSheetAction.openReceipt) {
      unawaited(
        ref
            .read(goRouterProvider)
            .push(AppRoutes.creditNoteReceiptFor(note.code)),
      );
    }
    ref.read(creditNotesControllerProvider.notifier).selectNote(null);
  }
}

const List<String> _statusOptions = [
  'active',
  'voided',
  'expired',
  'fullyRedeemed',
];

String _statusLabel(AppLocalizations l10n, String status) {
  switch (status) {
    case 'active':
      return l10n.creditNotesReceiptStatusActive;
    case 'voided':
      return l10n.creditNotesReceiptStatusVoided;
    case 'expired':
      return l10n.creditNotesReceiptStatusExpired;
    default:
      return status;
  }
}

String _localizeFailure(AppLocalizations l10n, Failure failure) {
  return failure.when(
    validation: (String? message, Map<String, List<String>>? _) =>
        message ?? l10n.customersErrorGeneric,
    unauthorized: (String? _) => l10n.customersErrorUnauthorized,
    forbidden: (String? _) => l10n.customersErrorForbidden,
    notFound: (String? _) => l10n.customersErrorGeneric,
    server: (String? message, int? _) => message ?? l10n.customersErrorGeneric,
    network: (String? _) => l10n.customersErrorNetwork,
    timeout: (String? _) => l10n.customersErrorTimeout,
    serialization: (String? _) => l10n.customersErrorGeneric,
    unknown: (String? _) => l10n.customersErrorGeneric,
  );
}

class _CreditNoteCard extends StatelessWidget {
  const _CreditNoteCard({required this.note, required this.onTap});

  final CreditNote note;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    final expiry = note.expiresAt == null
        ? l10n.creditNotesNoExpiry
        : DateFormat('dd MMM yyyy').format(note.expiresAt!);
    final statusColors = _creditNoteStatusColors(theme, note.status);
    final customerLabel = note.customerName ?? l10n.salesHistoryWalkInCustomer;

    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      child: InkWell(
        borderRadius: BorderRadius.circular(22),
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          note.code,
                          style: theme.textTheme.titleSmall?.copyWith(
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                        const SizedBox(height: 4),
                        Text(
                          customerLabel,
                          style: theme.textTheme.bodyMedium,
                        ),
                        const SizedBox(height: 2),
                        Text(
                          note.invoiceNumber,
                          style: theme.textTheme.bodySmall?.copyWith(
                            color: theme.colorScheme.onSurfaceVariant,
                          ),
                        ),
                      ],
                    ),
                  ),
                  Container(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 8,
                      vertical: 4,
                    ),
                    decoration: BoxDecoration(
                      color: statusColors.background,
                      borderRadius: BorderRadius.circular(20),
                    ),
                    child: Text(
                      _statusLabel(l10n, note.status),
                      style: theme.textTheme.labelSmall?.copyWith(
                        color: statusColors.foreground,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 8),
              Text(
                expiry,
                style: theme.textTheme.bodySmall?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

({Color background, Color foreground}) _creditNoteStatusColors(
  ThemeData theme,
  String status,
) {
  switch (status) {
    case 'active':
      return (
        background: theme.colorScheme.primaryContainer.withValues(alpha: 0.55),
        foreground: theme.colorScheme.primary,
      );
    case 'voided':
      return (
        background: theme.colorScheme.errorContainer,
        foreground: theme.colorScheme.error,
      );
    case 'expired':
      return (
        background: theme.colorScheme.surfaceContainerHighest,
        foreground: theme.colorScheme.onSurfaceVariant,
      );
    default:
      return (
        background: theme.colorScheme.secondaryContainer.withValues(alpha: 0.5),
        foreground: theme.colorScheme.secondary,
      );
  }
}

class _CreditNoteDetailSheet extends StatefulWidget {
  const _CreditNoteDetailSheet({
    required this.note,
    required this.canVoid,
    required this.onOpenReceipt,
    required this.onVoid,
  });

  final CreditNote note;
  final bool canVoid;
  final VoidCallback onOpenReceipt;
  final Future<bool> Function(String reason) onVoid;

  @override
  State<_CreditNoteDetailSheet> createState() => _CreditNoteDetailSheetState();
}

class _CreditNoteDetailSheetState extends State<_CreditNoteDetailSheet> {
  final _reasonController = TextEditingController();
  bool _isSubmitting = false;

  @override
  void dispose() {
    _reasonController.dispose();
    super.dispose();
  }

  Future<void> _submitVoid(BuildContext context) async {
    final reason = _reasonController.text.trim();
    if (_isSubmitting ||
        reason.isEmpty ||
        !widget.canVoid ||
        !widget.note.isActive) {
      return;
    }
    setState(() => _isSubmitting = true);
    final isSuccess = await widget.onVoid(reason);
    if (!context.mounted) return;
    setState(() => _isSubmitting = false);
    if (!isSuccess) return;
    Navigator.of(context).pop(_CreditNoteDetailSheetAction.voided);
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 0, 16, 24),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            widget.note.code,
            style: theme.textTheme.titleLarge,
          ),
          const SizedBox(height: 8),
          Text(widget.note.reason),
          const SizedBox(height: 12),
          Text('${l10n.creditNotesInvoiceLabel} ${widget.note.invoiceNumber}'),
          Text('${l10n.creditNotesReturnLabel} ${widget.note.returnNumber}'),
          Text(
            '${l10n.creditNotesBalanceLabel} '
            '${widget.note.availableBalance.toStringAsFixed(2)}',
          ),
          const SizedBox(height: 12),
          TextButton(
            key: const Key('credit-note-open-receipt-button'),
            onPressed: widget.onOpenReceipt,
            child: Text(l10n.creditNotesOpenReceipt),
          ),
          if (widget.canVoid && widget.note.isActive) ...[
            const SizedBox(height: 12),
            TextField(
              controller: _reasonController,
              decoration: InputDecoration(
                labelText: l10n.creditNotesVoidReason,
                border: const OutlineInputBorder(),
              ),
            ),
          ],
          const SizedBox(height: 12),
          Row(
            mainAxisAlignment: MainAxisAlignment.end,
            children: [
              if (widget.canVoid && widget.note.isActive)
                TextButton(
                  onPressed: _isSubmitting
                      ? null
                      : () => unawaited(_submitVoid(context)),
                  child: _isSubmitting
                      ? const SizedBox(
                          height: 16,
                          width: 16,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : Text(l10n.creditNotesVoid),
                ),
              TextButton(
                onPressed: () => Navigator.of(context).pop(),
                child: Text(l10n.creditNotesClose),
              ),
            ],
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
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);

    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              Icons.error_outline,
              size: 48,
              color: theme.colorScheme.onSurfaceVariant,
            ),
            const SizedBox(height: 12),
            Text(
              l10n.shellManageCreditNotes,
              style: const TextStyle(
                fontSize: 18,
                fontWeight: FontWeight.w600,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 8),
            Text(message, textAlign: TextAlign.center),
            const SizedBox(height: 16),
            FilledButton(
              onPressed: onRetry,
              child: Text(l10n.creditNotesRetry),
            ),
          ],
        ),
      ),
    );
  }
}

enum _CreditNoteDetailSheetAction { openReceipt, voided }

class _EmptyState extends StatelessWidget {
  const _EmptyState({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              Icons.receipt_long_outlined,
              size: 48,
              color: theme.colorScheme.onSurfaceVariant,
            ),
            const SizedBox(height: 12),
            Text(
              message,
              style: const TextStyle(
                fontSize: 18,
                fontWeight: FontWeight.w600,
              ),
              textAlign: TextAlign.center,
            ),
          ],
        ),
      ),
    );
  }
}
