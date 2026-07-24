import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_draft.dart';
import 'package:intelibill_mobile/src/features/discounts/domain/entities/discount_rule_query.dart';
import 'package:intelibill_mobile/src/features/discounts/presentation/controllers/discount_rule_editor_controller.dart';
import 'package:intl/intl.dart';

Future<bool?> showCreateDiscountRuleSheet(BuildContext context) {
  return showModalBottomSheet<bool>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    useSafeArea: true,
    builder: (_) => const CreateDiscountRuleSheet(),
  );
}

class CreateDiscountRuleSheet extends ConsumerStatefulWidget {
  const CreateDiscountRuleSheet({super.key});

  static const ruleTypeFieldKey = Key('create-discount-rule-type');
  static const nameFieldKey = Key('create-discount-name');
  static const descriptionFieldKey = Key('create-discount-description');
  static const percentageFieldKey = Key('create-discount-percentage');
  static const thresholdFieldKey = Key('create-discount-threshold');
  static const batchFieldKey = Key('create-discount-batch');
  static const startsAtFieldKey = Key('create-discount-starts-at');
  static const endsAtFieldKey = Key('create-discount-ends-at');
  static const belowCostCheckboxKey = Key('create-discount-below-cost');
  static const belowCostReasonFieldKey = Key(
    'create-discount-below-cost-reason',
  );
  static const previewButtonKey = Key('create-discount-preview');
  static const submitButtonKey = Key('create-discount-submit');
  static const cancelButtonKey = Key('create-discount-cancel');

  @override
  ConsumerState<CreateDiscountRuleSheet> createState() =>
      _CreateDiscountRuleSheetState();
}

class _CreateDiscountRuleSheetState
    extends ConsumerState<CreateDiscountRuleSheet> {
  final _formKey = GlobalKey<FormState>();
  final _nameController = TextEditingController();
  final _descriptionController = TextEditingController();
  final _percentageController = TextEditingController(text: '10');
  final _thresholdController = TextEditingController();
  final _belowCostReasonController = TextEditingController();

  String _ruleType = DiscountRuleTypeFilter.salePercentage;
  String? _inventoryBatchId;
  DateTime? _startsAt;
  DateTime? _endsAt;
  bool _belowCostConfirmed = false;

  @override
  void initState() {
    super.initState();
    unawaited(
      ref.read(discountRuleEditorControllerProvider.notifier).loadBatches(),
    );
  }

  @override
  void dispose() {
    _nameController.dispose();
    _descriptionController.dispose();
    _percentageController.dispose();
    _thresholdController.dispose();
    _belowCostReasonController.dispose();
    super.dispose();
  }

  bool get _isBatchRule => _ruleType == DiscountRuleTypeFilter.batchPercentage;

  bool get _isThresholdRule =>
      _ruleType == DiscountRuleTypeFilter.saleThresholdPercentage;

  CreateDiscountRuleInput _buildCreateInput() {
    final description = _descriptionController.text.trim();
    final reason = _belowCostReasonController.text.trim();
    return CreateDiscountRuleInput(
      ruleType: _ruleType,
      name: _nameController.text.trim(),
      description: description.isEmpty ? null : description,
      inventoryBatchId: _isBatchRule ? _inventoryBatchId : null,
      percentage: double.parse(_percentageController.text.trim()),
      thresholdAmount: _isThresholdRule
          ? double.parse(_thresholdController.text.trim())
          : null,
      startsAt: _startsAt,
      endsAt: _endsAt,
      belowCostConfirmed: _belowCostConfirmed,
      belowCostConfirmationReason: reason.isEmpty ? null : reason,
    );
  }

  PreviewDiscountRuleInput _buildPreviewInput({
    required bool belowCostConfirmed,
  }) {
    return PreviewDiscountRuleInput(
      ruleType: _ruleType,
      percentage: double.parse(_percentageController.text.trim()),
      thresholdAmount: _isThresholdRule
          ? double.tryParse(_thresholdController.text.trim())
          : null,
      inventoryBatchId: _isBatchRule ? _inventoryBatchId : null,
      startsAt: _startsAt,
      endsAt: _endsAt,
      belowCostConfirmed: belowCostConfirmed,
    );
  }

  Future<void> _runPreview() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    await ref
        .read(discountRuleEditorControllerProvider.notifier)
        .preview(_buildPreviewInput(belowCostConfirmed: _belowCostConfirmed));
  }

  Future<void> _submit() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    final success = await ref
        .read(discountRuleEditorControllerProvider.notifier)
        .create(_buildCreateInput());
    if (!mounted) return;
    if (success) {
      Navigator.of(context).pop(true);
    }
  }

  Future<void> _pickDate({required bool isStart}) async {
    final initial = (isStart ? _startsAt : _endsAt) ?? DateTime.now();
    final picked = await showDatePicker(
      context: context,
      initialDate: initial,
      firstDate: DateTime(2020),
      lastDate: DateTime(2100),
    );
    if (picked == null || !mounted) return;
    setState(() {
      if (isStart) {
        _startsAt = picked;
      } else {
        _endsAt = picked;
      }
    });
    ref.read(discountRuleEditorControllerProvider.notifier).clearMessages();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final theme = Theme.of(context);
    final state = ref.watch(discountRuleEditorControllerProvider);
    final disabled = state.isSubmitting || state.isPreviewLoading;
    final dateFormat = DateFormat('dd MMM yyyy');

    return SafeArea(
      child: Padding(
        padding: EdgeInsets.fromLTRB(
          20,
          8,
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
                  l10n.discountsCreateTitle,
                  style: theme.textTheme.titleLarge?.copyWith(
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  l10n.discountsCreateSubtitle,
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: theme.colorScheme.onSurfaceVariant,
                  ),
                ),
                const SizedBox(height: 16),
                if (state.localValidationMessage != null)
                  _MessageBanner(
                    message: _localizeLocalMessage(
                      l10n,
                      state.localValidationMessage!,
                    ),
                  ),
                if (state.previewFailure != null)
                  _MessageBanner(
                    message: _localizeFailure(l10n, state.previewFailure!),
                  ),
                if (state.submitFailure != null)
                  _MessageBanner(
                    message: _localizeFailure(l10n, state.submitFailure!),
                  ),
                DropdownButtonFormField<String>(
                  key: CreateDiscountRuleSheet.ruleTypeFieldKey,
                  initialValue: _ruleType,
                  decoration: InputDecoration(
                    labelText: l10n.discountsCreateRuleTypeLabel,
                    border: const OutlineInputBorder(),
                  ),
                  items: [
                    DropdownMenuItem(
                      value: DiscountRuleTypeFilter.batchPercentage,
                      child: Text(l10n.discountsCreateRuleTypeBatch),
                    ),
                    DropdownMenuItem(
                      value: DiscountRuleTypeFilter.salePercentage,
                      child: Text(l10n.discountsCreateRuleTypeSale),
                    ),
                    DropdownMenuItem(
                      value: DiscountRuleTypeFilter.saleThresholdPercentage,
                      child: Text(l10n.discountsCreateRuleTypeThreshold),
                    ),
                  ],
                  onChanged: disabled
                      ? null
                      : (value) {
                          if (value == null) return;
                          setState(() {
                            _ruleType = value;
                            if (!_isBatchRule) _inventoryBatchId = null;
                            if (!_isThresholdRule) {
                              _thresholdController.clear();
                            }
                          });
                          ref
                              .read(
                                discountRuleEditorControllerProvider.notifier,
                              )
                              .clearMessages();
                        },
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: CreateDiscountRuleSheet.nameFieldKey,
                  controller: _nameController,
                  enabled: !disabled,
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.discountsCreateNameLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) {
                    final trimmed = (value ?? '').trim();
                    if (trimmed.isEmpty) {
                      return l10n.discountsCreateNameRequired;
                    }
                    if (trimmed.length > 200) {
                      return l10n.discountsCreateNameMax;
                    }
                    return null;
                  },
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: CreateDiscountRuleSheet.descriptionFieldKey,
                  controller: _descriptionController,
                  enabled: !disabled,
                  maxLines: 2,
                  decoration: InputDecoration(
                    labelText: l10n.discountsCreateDescriptionLabel,
                    border: const OutlineInputBorder(),
                  ),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: CreateDiscountRuleSheet.percentageFieldKey,
                  controller: _percentageController,
                  enabled: !disabled,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  decoration: InputDecoration(
                    labelText: l10n.discountsCreatePercentageLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) {
                    final parsed = double.tryParse((value ?? '').trim());
                    if (parsed == null || parsed <= 0 || parsed > 100) {
                      return l10n.discountsCreatePercentageInvalid;
                    }
                    return null;
                  },
                ),
                if (_isThresholdRule) ...[
                  const SizedBox(height: 12),
                  TextFormField(
                    key: CreateDiscountRuleSheet.thresholdFieldKey,
                    controller: _thresholdController,
                    enabled: !disabled,
                    keyboardType: const TextInputType.numberWithOptions(
                      decimal: true,
                    ),
                    decoration: InputDecoration(
                      labelText: l10n.discountsCreateThresholdLabel,
                      border: const OutlineInputBorder(),
                    ),
                    validator: (value) {
                      final parsed = double.tryParse((value ?? '').trim());
                      if (parsed == null || parsed <= 0) {
                        return l10n.discountsCreateThresholdInvalid;
                      }
                      return null;
                    },
                  ),
                ],
                if (_isBatchRule) ...[
                  const SizedBox(height: 12),
                  if (state.isLoadingBatches)
                    const LinearProgressIndicator(minHeight: 2),
                  if (state.batchesFailure != null)
                    _MessageBanner(
                      message: _localizeFailure(l10n, state.batchesFailure!),
                      onRetry: disabled
                          ? null
                          : () => unawaited(
                              ref
                                  .read(
                                    discountRuleEditorControllerProvider
                                        .notifier,
                                  )
                                  .loadBatches(force: true),
                            ),
                    ),
                  DropdownButtonFormField<String>(
                    key: CreateDiscountRuleSheet.batchFieldKey,
                    initialValue: _inventoryBatchId,
                    isExpanded: true,
                    decoration: InputDecoration(
                      labelText: l10n.discountsCreateBatchLabel,
                      border: const OutlineInputBorder(),
                    ),
                    items: state.selectableBatches
                        .map(
                          (batch) => DropdownMenuItem(
                            value: batch.batchId,
                            child: Text(
                              '${batch.itemName} · ${batch.batchNumber}',
                              overflow: TextOverflow.ellipsis,
                            ),
                          ),
                        )
                        .toList(),
                    onChanged: disabled
                        ? null
                        : (value) {
                            setState(() => _inventoryBatchId = value);
                            ref
                                .read(
                                  discountRuleEditorControllerProvider.notifier,
                                )
                                .clearMessages();
                          },
                    validator: (value) {
                      if (!_isBatchRule) return null;
                      if (value == null || value.isEmpty) {
                        return l10n.discountsCreateBatchRequired;
                      }
                      return null;
                    },
                  ),
                ],
                const SizedBox(height: 12),
                Row(
                  children: [
                    Expanded(
                      child: OutlinedButton.icon(
                        key: CreateDiscountRuleSheet.startsAtFieldKey,
                        onPressed: disabled
                            ? null
                            : () => unawaited(_pickDate(isStart: true)),
                        icon: const Icon(Icons.event, size: 18),
                        label: Text(
                          _startsAt == null
                              ? l10n.discountsCreateStartsAtLabel
                              : dateFormat.format(_startsAt!),
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: OutlinedButton.icon(
                        key: CreateDiscountRuleSheet.endsAtFieldKey,
                        onPressed: disabled
                            ? null
                            : () => unawaited(_pickDate(isStart: false)),
                        icon: const Icon(Icons.event, size: 18),
                        label: Text(
                          _endsAt == null
                              ? l10n.discountsCreateEndsAtLabel
                              : dateFormat.format(_endsAt!),
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                    ),
                  ],
                ),
                if (state.preview != null) ...[
                  const SizedBox(height: 16),
                  _PreviewCard(preview: state.preview!, l10n: l10n),
                ],
                if (state.preview?.needsBelowCostConfirmation == true) ...[
                  const SizedBox(height: 12),
                  CheckboxListTile(
                    key: CreateDiscountRuleSheet.belowCostCheckboxKey,
                    contentPadding: EdgeInsets.zero,
                    value: _belowCostConfirmed,
                    onChanged: disabled
                        ? null
                        : (value) {
                            setState(
                              () => _belowCostConfirmed = value ?? false,
                            );
                            ref
                                .read(
                                  discountRuleEditorControllerProvider.notifier,
                                )
                                .clearMessages();
                          },
                    title: Text(l10n.discountsCreateBelowCostConfirm),
                    controlAffinity: ListTileControlAffinity.leading,
                  ),
                  TextFormField(
                    key: CreateDiscountRuleSheet.belowCostReasonFieldKey,
                    controller: _belowCostReasonController,
                    enabled: !disabled,
                    decoration: InputDecoration(
                      labelText: l10n.discountsCreateBelowCostReasonLabel,
                      border: const OutlineInputBorder(),
                    ),
                  ),
                ],
                const SizedBox(height: 16),
                Row(
                  children: [
                    Expanded(
                      child: OutlinedButton(
                        key: CreateDiscountRuleSheet.previewButtonKey,
                        onPressed: disabled
                            ? null
                            : () => unawaited(_runPreview()),
                        child: state.isPreviewLoading
                            ? const SizedBox(
                                width: 18,
                                height: 18,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                ),
                              )
                            : Text(l10n.discountsCreatePreview),
                      ),
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: FilledButton(
                        key: CreateDiscountRuleSheet.submitButtonKey,
                        onPressed: disabled ? null : () => unawaited(_submit()),
                        child: state.isSubmitting
                            ? const SizedBox(
                                width: 18,
                                height: 18,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                  color: Colors.white,
                                ),
                              )
                            : Text(l10n.discountsCreateSubmit),
                      ),
                    ),
                  ],
                ),
                TextButton(
                  key: CreateDiscountRuleSheet.cancelButtonKey,
                  onPressed: disabled
                      ? null
                      : () => Navigator.of(context).pop(false),
                  child: Text(l10n.commonCancel),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _PreviewCard extends StatelessWidget {
  const _PreviewCard({required this.preview, required this.l10n});

  final DiscountRulePreview preview;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        border: Border.all(color: theme.colorScheme.outlineVariant),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            l10n.discountsCreatePreviewTitle,
            style: theme.textTheme.titleSmall?.copyWith(
              fontWeight: FontWeight.w600,
            ),
          ),
          const SizedBox(height: 8),
          Text(l10n.discountsCreatePreviewAffected(preview.affectedCount)),
          if (preview.safeMaxPercentage != null)
            Text(
              l10n.discountsCreatePreviewSafeMax(
                preview.safeMaxPercentage!.toStringAsFixed(2),
              ),
            ),
          if (preview.errors.isNotEmpty) ...[
            const SizedBox(height: 8),
            Text(
              l10n.discountsCreatePreviewErrors,
              style: TextStyle(color: theme.colorScheme.error),
            ),
            for (final error in preview.errors)
              Text(
                '• ${error.message}',
                style: TextStyle(color: theme.colorScheme.error),
              ),
          ],
          if (preview.belowCostSample.isNotEmpty) ...[
            const SizedBox(height: 8),
            Text(l10n.discountsCreatePreviewBelowCost),
            for (final sample in preview.belowCostSample)
              Text('• ${sample.itemName} (${sample.batchNumber})'),
          ],
        ],
      ),
    );
  }
}

class _MessageBanner extends StatelessWidget {
  const _MessageBanner({required this.message, this.onRetry});

  final String message;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Container(
        padding: const EdgeInsets.all(12),
        decoration: BoxDecoration(
          color: colorScheme.errorContainer,
          borderRadius: BorderRadius.circular(8),
        ),
        child: Row(
          children: [
            Icon(Icons.error_outline, color: colorScheme.error, size: 20),
            const SizedBox(width: 8),
            Expanded(
              child: Text(
                message,
                style: TextStyle(color: colorScheme.onErrorContainer),
              ),
            ),
            if (onRetry != null)
              TextButton(
                onPressed: onRetry,
                child: Text(AppLocalizations.of(context)!.discountsRetry),
              ),
          ],
        ),
      ),
    );
  }
}

String _localizeLocalMessage(AppLocalizations l10n, String code) {
  switch (code) {
    case 'belowCostConfirmationRequired':
      return l10n.discountsCreateBelowCostRequired;
    case 'belowCostReasonRequired':
      return l10n.discountsCreateBelowCostReasonRequired;
    default:
      return code;
  }
}

String _localizeFailure(AppLocalizations l10n, Failure failure) {
  return failure.when(
    validation: (String? message, Map<String, List<String>>? _) =>
        message ?? l10n.discountsCreateErrorGeneric,
    unauthorized: (String? _) => l10n.discountsErrorUnauthorized,
    forbidden: (String? _) => l10n.discountsErrorForbidden,
    notFound: (String? _) => l10n.discountsCreateErrorGeneric,
    server: (String? message, int? _) =>
        message ?? l10n.discountsCreateErrorGeneric,
    network: (String? _) => l10n.discountsErrorNetwork,
    timeout: (String? _) => l10n.discountsErrorTimeout,
    serialization: (String? _) => l10n.discountsCreateErrorGeneric,
    unknown: (String? _) => l10n.discountsCreateErrorGeneric,
  );
}
