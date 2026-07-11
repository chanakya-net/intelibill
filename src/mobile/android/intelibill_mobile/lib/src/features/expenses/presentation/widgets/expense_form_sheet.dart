import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/expenses/presentation/controllers/expenses_controller.dart';

class ExpenseFormSheet extends ConsumerStatefulWidget {
  const ExpenseFormSheet({super.key});

  static const categoryFieldKey = Key('expense-form-category');
  static const amountFieldKey = Key('expense-form-amount');
  static const dateFieldKey = Key('expense-form-date');
  static const paidToFieldKey = Key('expense-form-paid-to');
  static const descriptionFieldKey = Key('expense-form-description');
  static const categoryRetryButtonKey = Key('expense-form-category-retry');
  static const submitButtonKey = Key('expense-form-submit');
  static const cancelButtonKey = Key('expense-form-cancel');

  @override
  ConsumerState<ExpenseFormSheet> createState() => _ExpenseFormSheetState();
}

class _ExpenseFormSheetState extends ConsumerState<ExpenseFormSheet> {
  final _formKey = GlobalKey<FormState>();
  final _categoryController = TextEditingController();
  final _amountController = TextEditingController();
  final _paidToController = TextEditingController();
  final _descriptionController = TextEditingController();
  late DateTime _expenseDate;

  @override
  void initState() {
    super.initState();
    final now = DateTime.now();
    _expenseDate = DateTime(now.year, now.month, now.day);
    unawaited(
      ref.read(expensesControllerProvider.notifier).loadCategories(),
    );
  }

  @override
  void dispose() {
    _categoryController.dispose();
    _amountController.dispose();
    _paidToController.dispose();
    _descriptionController.dispose();
    super.dispose();
  }

  Future<void> _selectDate() async {
    final selected = await showDatePicker(
      context: context,
      initialDate: _expenseDate,
      firstDate: DateTime(2000),
      lastDate: DateTime(2100),
    );
    if (selected != null && mounted) setState(() => _expenseDate = selected);
  }

  Future<void> _submit() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    final description = _descriptionController.text.trim();
    final success = await ref
        .read(expensesControllerProvider.notifier)
        .recordExpense(
          categoryName: _categoryController.text.trim(),
          amount: double.parse(_amountController.text.trim()),
          paidTo: _paidToController.text.trim(),
          description: description.isEmpty ? null : description,
          expenseDate: _expenseDate,
        );
    if (mounted && success) Navigator.of(context).pop(true);
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(expensesControllerProvider);
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    final disabled = state.isSubmitting;
    final suggestions = _suggestions(state);

    return SafeArea(
      child: Padding(
        padding: EdgeInsets.fromLTRB(
          20,
          20,
          20,
          20 + MediaQuery.viewInsetsOf(context).bottom,
        ),
        child: Form(
          key: _formKey,
          autovalidateMode: AutovalidateMode.onUserInteraction,
          child: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text(
                  l10n.expensesRecordExpense,
                  style: theme.textTheme.titleLarge?.copyWith(
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const SizedBox(height: 16),
                if (state.categoryFailure != null)
                  _ErrorBanner(
                    message: _localizeCategoryFailure(
                      l10n,
                      state.categoryFailure!,
                    ),
                    retryKey: ExpenseFormSheet.categoryRetryButtonKey,
                    retryLabel: l10n.expensesCategoryRetry,
                    onRetry: disabled
                        ? null
                        : () => unawaited(
                            ref
                                .read(expensesControllerProvider.notifier)
                                .loadCategories(force: true),
                          ),
                  ),
                if (state.submitFailure != null)
                  _ErrorBanner(
                    message: _localizeSubmitFailure(
                      l10n,
                      state.submitFailure!,
                    ),
                  ),
                TextFormField(
                  key: ExpenseFormSheet.categoryFieldKey,
                  controller: _categoryController,
                  enabled: !disabled,
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.expensesCategoryLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) => _validateText(
                    value,
                    requiredMessage: l10n.expensesCategoryRequired,
                    maxMessage: l10n.expensesCategoryMax,
                    maxLength: 100,
                  ),
                  onChanged: (_) => setState(() {}),
                ),
                if (state.isLoadingCategories)
                  const LinearProgressIndicator(minHeight: 2),
                if (suggestions.isNotEmpty) ...[
                  const SizedBox(height: 8),
                  Wrap(
                    spacing: 8,
                    runSpacing: 4,
                    children: suggestions
                        .map(
                          (name) => ActionChip(
                            key: Key('expense-category-suggestion-$name'),
                            label: Text(name),
                            onPressed: disabled
                                ? null
                                : () {
                                    _categoryController.text = name;
                                    _categoryController.selection =
                                        TextSelection.collapsed(
                                          offset: name.length,
                                        );
                                    setState(() {});
                                  },
                          ),
                        )
                        .toList(),
                  ),
                ],
                const SizedBox(height: 12),
                TextFormField(
                  key: ExpenseFormSheet.amountFieldKey,
                  controller: _amountController,
                  enabled: !disabled,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.expensesAmountLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) => _validateAmount(
                    value,
                    requiredMessage: l10n.expensesAmountRequired,
                    invalidMessage: l10n.expensesAmountInvalid,
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: ExpenseFormSheet.paidToFieldKey,
                  controller: _paidToController,
                  enabled: !disabled,
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.expensesPaidToLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) => _validateText(
                    value,
                    requiredMessage: l10n.expensesPaidToRequired,
                    maxMessage: l10n.expensesPaidToMax,
                    maxLength: 255,
                  ),
                ),
                const SizedBox(height: 12),
                InkWell(
                  key: ExpenseFormSheet.dateFieldKey,
                  onTap: disabled ? null : _selectDate,
                  child: InputDecorator(
                    decoration: InputDecoration(
                      labelText: l10n.expensesDateLabel,
                      border: const OutlineInputBorder(),
                    ),
                    child: Text(
                      MaterialLocalizations.of(
                        context,
                      ).formatMediumDate(_expenseDate),
                    ),
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: ExpenseFormSheet.descriptionFieldKey,
                  controller: _descriptionController,
                  enabled: !disabled,
                  maxLines: 3,
                  decoration: InputDecoration(
                    labelText: l10n.expensesDescriptionLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) => _validateOptional(
                    value,
                    maxMessage: l10n.expensesDescriptionMax,
                    maxLength: 500,
                  ),
                ),
                const SizedBox(height: 16),
                Row(
                  children: [
                    TextButton(
                      key: ExpenseFormSheet.cancelButtonKey,
                      onPressed: disabled
                          ? null
                          : () => Navigator.of(context).pop(false),
                      child: Text(l10n.commonCancel),
                    ),
                    const Spacer(),
                    FilledButton(
                      key: ExpenseFormSheet.submitButtonKey,
                      onPressed: disabled ? null : _submit,
                      child: disabled
                          ? const SizedBox(
                              width: 16,
                              height: 16,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : Text(l10n.expensesRecordExpense),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  List<String> _suggestions(ExpensesState state) {
    final query = _categoryController.text.trim().toLowerCase();
    final seen = <String>{};
    return state.categories
        .map((category) => category.name.trim())
        .where((name) => name.isNotEmpty)
        .where((name) => name.toLowerCase() != 'supplier payments')
        .where((name) => query.isEmpty || name.toLowerCase().contains(query))
        .where((name) => seen.add(name.toLowerCase()))
        .toList();
  }
}

class _ErrorBanner extends StatelessWidget {
  const _ErrorBanner({
    required this.message,
    this.retryKey,
    this.retryLabel,
    this.onRetry,
  });

  final String message;
  final Key? retryKey;
  final String? retryLabel;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    return Container(
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(12),
        color: colors.errorContainer,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(message, style: TextStyle(color: colors.onErrorContainer)),
          if (onRetry != null)
            TextButton(
              key: retryKey,
              onPressed: onRetry,
              child: Text(retryLabel!),
            ),
        ],
      ),
    );
  }
}

String? _validateText(
  String? value, {
  required String requiredMessage,
  required String maxMessage,
  required int maxLength,
}) {
  final trimmed = (value ?? '').trim();
  if (trimmed.isEmpty) return requiredMessage;
  return trimmed.length > maxLength ? maxMessage : null;
}

String? _validateOptional(
  String? value, {
  required String maxMessage,
  required int maxLength,
}) {
  final trimmed = (value ?? '').trim();
  return trimmed.length > maxLength ? maxMessage : null;
}

String? _validateAmount(
  String? value, {
  required String requiredMessage,
  required String invalidMessage,
}) {
  final trimmed = (value ?? '').trim();
  if (trimmed.isEmpty) return requiredMessage;
  final amount = double.tryParse(trimmed);
  return amount == null || amount <= 0 ? invalidMessage : null;
}

String _localizeCategoryFailure(AppLocalizations l10n, Failure failure) {
  return failure.when(
    validation: (message, _) => message ?? l10n.expensesCategoryLoadError,
    unauthorized: (_) => l10n.expensesCategoryLoadError,
    forbidden: (_) => l10n.expensesCategoryLoadError,
    notFound: (_) => l10n.expensesCategoryLoadError,
    server: (message, _) => message ?? l10n.expensesCategoryLoadError,
    network: (_) => l10n.expensesCategoryLoadError,
    timeout: (_) => l10n.expensesCategoryLoadError,
    serialization: (_) => l10n.expensesCategoryLoadError,
    unknown: (_) => l10n.expensesCategoryLoadError,
  );
}

String _localizeSubmitFailure(AppLocalizations l10n, Failure failure) {
  return failure.when(
    validation: (message, _) => message ?? l10n.expensesMutationErrorGeneric,
    unauthorized: (_) => l10n.expensesMutationErrorUnauthorized,
    forbidden: (_) => l10n.expensesMutationErrorForbidden,
    notFound: (_) => l10n.expensesMutationErrorGeneric,
    server: (message, _) => message ?? l10n.expensesMutationErrorGeneric,
    network: (_) => l10n.expensesMutationErrorNetwork,
    timeout: (_) => l10n.expensesMutationErrorTimeout,
    serialization: (_) => l10n.expensesMutationErrorGeneric,
    unknown: (_) => l10n.expensesMutationErrorGeneric,
  );
}
