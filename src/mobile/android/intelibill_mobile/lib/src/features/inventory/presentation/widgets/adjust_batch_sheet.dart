import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/inventory_batch.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/inventory_batches_controller.dart';

class AdjustBatchSheet extends ConsumerStatefulWidget {
  const AdjustBatchSheet({
    super.key,
    required this.batch,
    required this.canManage,
  });

  final InventoryBatch batch;
  final bool canManage;

  static const Key directionFieldKey = Key('adjust-batch-direction');
  static const Key reasonFieldKey = Key('adjust-batch-reason');
  static const Key quantityFieldKey = Key('adjust-batch-quantity');
  static const Key notesFieldKey = Key('adjust-batch-notes');
  static const Key saveButtonKey = Key('adjust-batch-save');

  @override
  ConsumerState<AdjustBatchSheet> createState() => _AdjustBatchSheetState();
}

class _AdjustBatchSheetState extends ConsumerState<AdjustBatchSheet> {
  final _formKey = GlobalKey<FormState>();
  final _quantityController = TextEditingController();
  final _notesController = TextEditingController();

  String _direction = 'Increase';
  String? _reason;
  DateTime? _performedAt;
  double? _parsedQty;

  static const _decreaseReasons = [
    'Damaged',
    'Expired',
    'Stolen',
    'MissingLost',
    'StockCountCorrection',
    'OtherLoss',
  ];

  static const _increaseReasons = [
    'FoundStock',
    'StockCountCorrection',
    'ReturnRestockCorrection',
    'OtherGain',
  ];

  List<String> get _currentReasons =>
      _direction == 'Decrease' ? _decreaseReasons : _increaseReasons;

  bool get _notesRequired => _reason == 'OtherLoss' || _reason == 'OtherGain';

  double get _previewQty {
    final q = _parsedQty ?? 0;
    if (_direction == 'Increase') return widget.batch.quantity + q;
    return (widget.batch.quantity - q).clamp(0, double.infinity);
  }

  String _formatQty(double qty) {
    return qty.toStringAsFixed(qty.truncateToDouble() == qty ? 0 : 2);
  }

  @override
  void dispose() {
    _quantityController.dispose();
    _notesController.dispose();
    super.dispose();
  }

  String _reasonLabel(AppLocalizations l10n, String reason) {
    return switch (reason) {
      'Damaged' => l10n.inventoryAdjustReasonDamaged,
      'Expired' => l10n.inventoryAdjustReasonExpired,
      'Stolen' => l10n.inventoryAdjustReasonStolen,
      'MissingLost' => l10n.inventoryAdjustReasonMissingLost,
      'StockCountCorrection' => l10n.inventoryAdjustReasonStockCountCorrection,
      'OtherLoss' => l10n.inventoryAdjustReasonOtherLoss,
      'FoundStock' => l10n.inventoryAdjustReasonFoundStock,
      'ReturnRestockCorrection' =>
        l10n.inventoryAdjustReasonReturnRestockCorrection,
      'OtherGain' => l10n.inventoryAdjustReasonOtherGain,
      _ => reason,
    };
  }

  Future<void> _pickDate() async {
    final now = DateTime.now();
    final picked = await showDatePicker(
      context: context,
      initialDate: _performedAt ?? now,
      firstDate: DateTime(now.year - 5),
      lastDate: now,
    );
    if (picked != null) {
      setState(() => _performedAt = picked);
    }
  }

  Future<void> _save() async {
    if (!widget.canManage) return;
    if (!(_formKey.currentState?.validate() ?? false)) return;
    final qty = _parsedQty;
    if (qty == null) return;

    unawaited(
      ref
          .read(inventoryBatchesControllerProvider.notifier)
          .adjustBatch(
            batchId: widget.batch.batchId,
            direction: _direction,
            reason: _reason!,
            quantity: qty,
            performedAt: _performedAt,
            notes: _notesController.text.trim().isEmpty
                ? null
                : _notesController.text.trim(),
          ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    final controllerState = ref.watch(inventoryBatchesControllerProvider);
    final isSubmitting = controllerState.isSubmitting;

    ref.listen(inventoryBatchesControllerProvider, (previous, next) {
      if (previous?.lastAdjustedBatchId != widget.batch.batchId &&
          next.lastAdjustedBatchId == widget.batch.batchId) {
        if (mounted) Navigator.of(context).pop(true);
      }
    });

    return Padding(
      padding: EdgeInsets.only(
        bottom: MediaQuery.of(context).viewInsets.bottom,
      ),
      child: Form(
        key: _formKey,
        child: SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                l10n.inventoryBatchesAdjustTitle,
                style: Theme.of(context).textTheme.titleLarge,
              ),
              const SizedBox(height: 4),
              Text(
                '${widget.batch.itemName} · ${widget.batch.batchNumber}',
                style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                  color: Theme.of(context).colorScheme.onSurfaceVariant,
                ),
              ),
              const SizedBox(height: 16),
              DropdownButtonFormField<String>(
                key: AdjustBatchSheet.directionFieldKey,
                initialValue: _direction,
                decoration: InputDecoration(
                  labelText: l10n.inventoryBatchesAdjustDirectionLabel,
                  border: const OutlineInputBorder(),
                ),
                items: [
                  DropdownMenuItem(
                    value: 'Increase',
                    child: Text(
                      l10n.inventoryBatchesAdjustDirectionIncrease,
                    ),
                  ),
                  DropdownMenuItem(
                    value: 'Decrease',
                    child: Text(
                      l10n.inventoryBatchesAdjustDirectionDecrease,
                    ),
                  ),
                ],
                onChanged: (value) {
                  if (value != null) {
                    setState(() {
                      _direction = value;
                      _reason = null;
                    });
                  }
                },
              ),
              const SizedBox(height: 12),
              DropdownButtonFormField<String>(
                key: ValueKey('reason-$_direction'),
                initialValue: _reason,
                decoration: InputDecoration(
                  labelText: l10n.inventoryBatchesAdjustReasonLabel,
                  border: const OutlineInputBorder(),
                ),
                items: _currentReasons
                    .map(
                      (r) => DropdownMenuItem(
                        value: r,
                        child: Text(_reasonLabel(l10n, r)),
                      ),
                    )
                    .toList(),
                validator: (v) => v == null
                    ? l10n.inventoryBatchesAdjustQuantityRequired
                    : null,
                onChanged: (value) => setState(() => _reason = value),
              ),
              const SizedBox(height: 12),
              TextFormField(
                key: AdjustBatchSheet.quantityFieldKey,
                controller: _quantityController,
                decoration: InputDecoration(
                  labelText: l10n.inventoryBatchesAdjustQuantityLabel,
                  border: const OutlineInputBorder(),
                ),
                keyboardType: const TextInputType.numberWithOptions(
                  decimal: true,
                ),
                inputFormatters: [
                  FilteringTextInputFormatter.allow(RegExp(r'^\d*\.?\d*')),
                ],
                onChanged: (v) {
                  setState(() => _parsedQty = double.tryParse(v));
                },
                validator: (v) {
                  if (v == null || v.trim().isEmpty) {
                    return l10n.inventoryBatchesAdjustQuantityRequired;
                  }
                  final q = double.tryParse(v);
                  if (q == null || q < 0.01) {
                    return l10n.inventoryBatchesAdjustQuantityMin;
                  }
                  if (_direction == 'Decrease' && q > widget.batch.quantity) {
                    return l10n.inventoryBatchesAdjustQuantityMax;
                  }
                  return null;
                },
              ),
              const SizedBox(height: 8),
              if (_parsedQty != null && _parsedQty! > 0)
                Padding(
                  padding: const EdgeInsets.only(bottom: 8),
                  child: Text(
                    '${l10n.inventoryBatchesAdjustPreviewLabel}: '
                    '${_formatQty(widget.batch.quantity)}'
                    ' → '
                    '${_formatQty(_previewQty)}',
                    style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                      color: Theme.of(context).colorScheme.primary,
                      fontWeight: FontWeight.w500,
                    ),
                  ),
                ),
              TextFormField(
                key: AdjustBatchSheet.notesFieldKey,
                controller: _notesController,
                decoration: InputDecoration(
                  labelText: l10n.inventoryBatchesAdjustNotesLabel,
                  border: const OutlineInputBorder(),
                ),
                maxLength: 500,
                maxLines: 3,
                validator: (v) {
                  if (_notesRequired && (v == null || v.trim().isEmpty)) {
                    return l10n.inventoryBatchesAdjustNotesRequired;
                  }
                  return null;
                },
              ),
              const SizedBox(height: 8),
              InkWell(
                onTap: _pickDate,
                child: InputDecorator(
                  decoration: InputDecoration(
                    labelText: l10n.inventoryBatchesAdjustPerformedAtLabel,
                    border: const OutlineInputBorder(),
                    suffixIcon: const Icon(Icons.calendar_today_outlined),
                  ),
                  child: Text(
                    _performedAt != null
                        ? '${_performedAt!.day.toString().padLeft(2, '0')}/'
                              '${_performedAt!.month.toString().padLeft(2, '0')}/'
                              '${_performedAt!.year}'
                        : '',
                  ),
                ),
              ),
              if (controllerState.submitFailure != null)
                Padding(
                  padding: const EdgeInsets.only(top: 12),
                  child: Text(
                    _localizeSubmitFailure(
                      l10n,
                      controllerState.submitFailure!,
                    ),
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                    ),
                  ),
                ),
              const SizedBox(height: 16),
              Row(
                mainAxisAlignment: MainAxisAlignment.end,
                children: [
                  TextButton(
                    onPressed: isSubmitting
                        ? null
                        : () => Navigator.of(context).pop(false),
                    child: Text(l10n.commonCancel),
                  ),
                  const SizedBox(width: 8),
                  FilledButton(
                    key: AdjustBatchSheet.saveButtonKey,
                    onPressed: (isSubmitting || !widget.canManage)
                        ? null
                        : _save,
                    child: isSubmitting
                        ? const SizedBox(
                            height: 16,
                            width: 16,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : Text(l10n.commonSave),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

String _localizeSubmitFailure(AppLocalizations l10n, Failure failure) {
  return failure.when(
    validation: (String? message, Map<String, List<String>>? _) =>
        message ?? l10n.inventoryBatchesErrorGeneric,
    unauthorized: (String? _) => l10n.inventoryBatchesErrorUnauthorized,
    forbidden: (String? _) => l10n.inventoryBatchesErrorForbidden,
    notFound: (String? _) => l10n.inventoryBatchesErrorGeneric,
    server: (String? message, int? _) =>
        message ?? l10n.inventoryBatchesErrorGeneric,
    network: (String? _) => l10n.inventoryBatchesErrorNetwork,
    timeout: (String? _) => l10n.inventoryBatchesErrorTimeout,
    serialization: (String? _) => l10n.inventoryBatchesErrorGeneric,
    unknown: (String? _) => l10n.inventoryBatchesErrorGeneric,
  );
}
