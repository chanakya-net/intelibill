import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/expenses/domain/entities/expense_detail.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/controllers/expenses_controller.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/widgets/expense_form_sheet.dart';
import 'package:intl/intl.dart';

final NumberFormat _amountFormat = NumberFormat.currency(
  locale: 'en_IN',
  symbol: '₹',
  decimalDigits: 2,
);
final DateFormat _dateFormat = DateFormat('MMM d, yyyy');
final DateFormat _auditTimeFormat = DateFormat('MMM d, yyyy, h:mm a');

Future<void> showExpenseDetailSheet(BuildContext context) {
  return showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    useSafeArea: true,
    builder: (_) => const ExpenseDetailSheet(),
  );
}

class ExpenseDetailSheet extends ConsumerWidget {
  const ExpenseDetailSheet({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final state = ref.watch(expensesControllerProvider);
    final l10n = AppLocalizations.of(context)!;

    if (state.isDetailLoading) {
      return const SafeArea(
        child: SizedBox(
          height: 220,
          child: Center(child: CircularProgressIndicator()),
        ),
      );
    }

    if (state.detailFailure != null) {
      return _DetailFailure(
        details: _localizeFailure(l10n, state.detailFailure!),
        onRetry: () => unawaited(
          ref.read(expensesControllerProvider.notifier).retryExpenseDetail(),
        ),
      );
    }

    final detail = state.selectedExpense;
    if (detail == null) {
      return SafeArea(
        child: SizedBox(
          height: 160,
          child: Center(child: Text(l10n.expensesDetailNoSelection)),
        ),
      );
    }

    return _ExpenseDetailContent(detail: detail);
  }
}

class _ExpenseDetailContent extends ConsumerWidget {
  const _ExpenseDetailContent({required this.detail});

  final ExpenseDetail detail;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context)!;
    final empty = l10n.expensesDetailNotLinked;
    final rows = [
      _DetailRow(l10n.expensesDetailExpenseId, detail.id),
      _DetailRow(l10n.expensesDetailShopId, detail.shopId),
      _DetailRow(l10n.expensesDetailCategoryId, detail.categoryId),
      _DetailRow(
        l10n.expensesDetailAmount,
        _amountFormat.format(detail.amount),
      ),
      _DetailRow(
        l10n.expensesDetailStatus,
        detail.isVoided ? l10n.expensesDetailVoided : l10n.expensesDetailActive,
        semanticLabel: l10n.expensesStatusSemantics(
          detail.isVoided
              ? l10n.expensesDetailVoided
              : l10n.expensesDetailActive,
        ),
      ),
      _DetailRow(l10n.expensesDetailCategory, detail.categoryName),
      _DetailRow(l10n.expensesDetailPaidTo, detail.paidTo),
      _DetailRow(
        l10n.expensesDetailDescription,
        detail.description?.trim().isNotEmpty ?? false
            ? detail.description!.trim()
            : l10n.expensesDetailNotProvided,
      ),
      _DetailRow(
        l10n.expensesDetailExpenseDate,
        _dateFormat.format(detail.expenseDate),
      ),
      _DetailRow(l10n.expensesDetailActor, detail.actorUserId),
      _DetailRow(
        l10n.expensesDetailCreatedAt,
        _auditTimeFormat.format(detail.createdAt),
      ),
      _DetailRow(l10n.expensesDetailSource, _sourceLabel(l10n, detail)),
      _DetailRow(
        l10n.expensesDetailOriginalExpense,
        detail.originalExpenseId ?? empty,
      ),
      _DetailRow(
        l10n.expensesDetailSupplierLedgerEntry,
        detail.supplierLedgerEntryId ?? empty,
      ),
    ];

    return SafeArea(
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 24),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  child: Text(
                    l10n.expensesDetailTitle,
                    style: Theme.of(context).textTheme.titleLarge?.copyWith(
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                ),
                if (!detail.isVoided)
                  TextButton(
                    onPressed: () => _openCorrectExpenseSheet(context, ref),
                    child: Text(l10n.expensesCorrectExpense),
                  ),
              ],
            ),
            const SizedBox(height: 16),
            for (var index = 0; index < rows.length; index++)
              _DetailRowView(
                row: rows[index],
                isLast: index == rows.length - 1,
              ),
          ],
        ),
      ),
    );
  }

  Future<void> _openCorrectExpenseSheet(
    BuildContext context,
    WidgetRef ref,
  ) async {
    final corrected = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      showDragHandle: true,
      builder: (_) => ExpenseFormSheet(expenseToCorrect: detail),
    );
    if (!context.mounted || corrected != true) return;
    final l10n = AppLocalizations.of(context)!;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(l10n.expensesCorrectSuccess)),
    );
    await ref.read(expensesControllerProvider.notifier).retryExpenseDetail();
  }
}

String _sourceLabel(AppLocalizations l10n, ExpenseDetail detail) {
  if (detail.supplierLedgerEntryId != null) {
    return l10n.expensesDetailSourceSupplierPayment;
  }
  if (detail.originalExpenseId != null) {
    return l10n.expensesDetailSourceCorrection;
  }
  return l10n.expensesDetailSourceManual;
}

class _DetailRowView extends StatelessWidget {
  const _DetailRowView({required this.row, this.isLast = false});

  final _DetailRow row;
  final bool isLast;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final content = Column(
      children: [
        Padding(
          padding: const EdgeInsets.symmetric(vertical: 10),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                flex: 2,
                child: Text(
                  row.label,
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: theme.colorScheme.onSurfaceVariant,
                    fontWeight: FontWeight.w500,
                  ),
                ),
              ),
              const SizedBox(width: 16),
              Expanded(
                flex: 3,
                child: Text(
                  row.value,
                  textAlign: TextAlign.end,
                  style: theme.textTheme.bodyMedium?.copyWith(
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ),
            ],
          ),
        ),
        if (!isLast) const Divider(height: 1),
      ],
    );
    final semanticLabel = row.semanticLabel;
    return semanticLabel == null
        ? content
        : Semantics(
            label: semanticLabel,
            child: ExcludeSemantics(child: content),
          );
  }
}

class _DetailFailure extends StatelessWidget {
  const _DetailFailure({required this.details, required this.onRetry});

  final String details;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(
                Icons.error_outline,
                size: 48,
                color: Color(0xFF9A6B45),
              ),
              const SizedBox(height: 12),
              Text(
                l10n.expensesDetailUnableToLoad,
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.titleMedium?.copyWith(
                  fontWeight: FontWeight.w600,
                ),
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

class _DetailRow {
  const _DetailRow(this.label, this.value, {this.semanticLabel});

  final String label;
  final String value;
  final String? semanticLabel;
}
