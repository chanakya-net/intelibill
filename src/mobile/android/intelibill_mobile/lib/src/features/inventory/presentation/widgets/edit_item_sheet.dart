import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/items_controller.dart';
import 'package:intelibill_mobile/src/shared/barcode_scanner/show_barcode_scanner.dart';

class EditItemSheet extends ConsumerStatefulWidget {
  const EditItemSheet({super.key, required this.item});

  final Item item;

  static const nameFieldKey = Key('edit-item-name');
  static const barcodeFieldKey = Key('edit-item-barcode');
  static const scanBarcodeButtonKey = Key('edit-item-scan-barcode');
  static const uomFieldKey = Key('edit-item-uom');
  static const descriptionFieldKey = Key('edit-item-description');
  static const activeSwitchKey = Key('edit-item-active');
  static const submitButtonKey = Key('edit-item-submit');
  static const cancelButtonKey = Key('edit-item-cancel');

  @override
  ConsumerState<EditItemSheet> createState() => _EditItemSheetState();
}

class _EditItemSheetState extends ConsumerState<EditItemSheet> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _nameController;
  late final TextEditingController _barcodeController;
  late final TextEditingController _uomController;
  late final TextEditingController _descriptionController;
  late bool _isActive;

  @override
  void initState() {
    super.initState();
    _nameController = TextEditingController(text: widget.item.name);
    _barcodeController = TextEditingController(text: widget.item.barcode);
    _uomController = TextEditingController(text: widget.item.uom);
    _descriptionController = TextEditingController(
      text: widget.item.description ?? '',
    );
    _isActive = widget.item.isActive;
  }

  @override
  void dispose() {
    _nameController.dispose();
    _barcodeController.dispose();
    _uomController.dispose();
    _descriptionController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    ref.listen(itemsControllerProvider, (previous, next) {
      final shouldClose =
          previous?.lastAction != 'updated' && next.lastAction == 'updated';
      if (shouldClose &&
          mounted &&
          Navigator.of(context).canPop() &&
          (ModalRoute.of(context)?.isCurrent ?? false)) {
        Navigator.of(context).pop(true);
      }
    });

    final l10n = AppLocalizations.of(context)!;
    final state = ref.watch(itemsControllerProvider);
    final theme = Theme.of(context);
    final isSubmitting = state.isSubmitting;

    return SafeArea(
      child: Padding(
        padding: EdgeInsets.only(
          left: 20,
          right: 20,
          top: 20,
          bottom: 20 + MediaQuery.of(context).viewInsets.bottom,
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
                  l10n.inventoryUpdateTitle,
                  style: theme.textTheme.titleLarge?.copyWith(
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const SizedBox(height: 16),
                if (state.submitFailure != null)
                  Container(
                    margin: const EdgeInsets.only(bottom: 12),
                    padding: const EdgeInsets.all(12),
                    decoration: BoxDecoration(
                      borderRadius: BorderRadius.circular(12),
                      border: Border.all(
                        color: theme.colorScheme.errorContainer,
                      ),
                      color: theme.colorScheme.errorContainer,
                    ),
                    child: Text(
                      _localizeSubmitFailure(l10n, state.submitFailure!),
                      style: theme.textTheme.bodyMedium?.copyWith(
                        color: theme.colorScheme.onErrorContainer,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                TextFormField(
                  key: EditItemSheet.nameFieldKey,
                  controller: _nameController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.inventoryCreateNameLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) {
                    final trimmed = (value ?? '').trim();
                    if (trimmed.isEmpty) {
                      return l10n.inventoryCreateNameRequired;
                    }
                    if (trimmed.length > 180) {
                      return l10n.inventoryCreateNameMax;
                    }
                    return null;
                  },
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: EditItemSheet.barcodeFieldKey,
                  controller: _barcodeController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.inventoryCreateBarcodeLabel,
                    border: const OutlineInputBorder(),
                    suffixIcon: IconButton(
                      key: EditItemSheet.scanBarcodeButtonKey,
                      icon: const Icon(Icons.qr_code),
                      onPressed: isSubmitting ? null : _scanBarcode,
                    ),
                  ),
                  validator: (value) {
                    final trimmed = (value ?? '').trim();
                    if (trimmed.isEmpty) {
                      return l10n.inventoryCreateBarcodeRequired;
                    }
                    if (trimmed.length > 120) {
                      return l10n.inventoryCreateBarcodeMax;
                    }
                    return null;
                  },
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: EditItemSheet.uomFieldKey,
                  controller: _uomController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.inventoryCreateUomLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) {
                    final trimmed = (value ?? '').trim();
                    if (trimmed.isEmpty) return l10n.inventoryCreateUomRequired;
                    if (trimmed.length > 40) return l10n.inventoryCreateUomMax;
                    return null;
                  },
                ),
                const SizedBox(height: 12),
                TextFormField(
                  key: EditItemSheet.descriptionFieldKey,
                  controller: _descriptionController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.newline,
                  maxLines: 3,
                  decoration: InputDecoration(
                    labelText: l10n.inventoryCreateDescriptionLabel,
                    border: const OutlineInputBorder(),
                  ),
                  validator: (value) {
                    final trimmed = (value ?? '').trim();
                    if (trimmed.length > 320) {
                      return l10n.inventoryCreateDescriptionMax;
                    }
                    return null;
                  },
                ),
                const SizedBox(height: 4),
                SwitchListTile(
                  key: EditItemSheet.activeSwitchKey,
                  value: _isActive,
                  onChanged: isSubmitting
                      ? null
                      : (value) => setState(() => _isActive = value),
                  title: Text(l10n.inventoryCreateProductActiveLabel),
                  contentPadding: EdgeInsets.zero,
                ),
                const SizedBox(height: 8),
                Row(
                  children: [
                    TextButton(
                      key: EditItemSheet.cancelButtonKey,
                      onPressed: isSubmitting
                          ? null
                          : () => Navigator.of(context).pop(false),
                      child: Text(l10n.commonCancel),
                    ),
                    const Spacer(),
                    FilledButton(
                      key: EditItemSheet.submitButtonKey,
                      onPressed: isSubmitting ? null : _submit,
                      child: isSubmitting
                          ? const SizedBox(
                              width: 16,
                              height: 16,
                              child: CircularProgressIndicator(
                                strokeWidth: 2,
                                color: Colors.white,
                              ),
                            )
                          : Text(l10n.commonSave),
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

  Future<void> _scanBarcode() async {
    final result = await showBarcodeScanner(context);
    if (result == null || result.value.isEmpty) return;

    if (!mounted) return;

    _barcodeController.text = result.value;
  }

  void _submit() {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    final description = _descriptionController.text.trim();
    unawaited(
      ref
          .read(itemsControllerProvider.notifier)
          .updateItem(
            itemId: widget.item.itemId,
            name: _nameController.text.trim(),
            barcode: _barcodeController.text.trim(),
            uom: _uomController.text.trim(),
            description: description.isEmpty ? null : description,
            isActive: _isActive,
          ),
    );
  }
}

String _localizeSubmitFailure(AppLocalizations l10n, Failure failure) {
  return failure.when(
    validation: (String? message, Map<String, List<String>>? _) =>
        message ?? l10n.inventoryCreateErrorGeneric,
    unauthorized: (String? _) => l10n.inventoryCreateErrorUnauthorized,
    forbidden: (String? _) => l10n.inventoryCreateErrorForbidden,
    notFound: (String? _) => l10n.inventoryCreateErrorGeneric,
    server: (String? message, int? _) =>
        message ?? l10n.inventoryCreateErrorGeneric,
    network: (String? _) => l10n.inventoryCreateErrorNetwork,
    timeout: (String? _) => l10n.inventoryCreateErrorTimeout,
    serialization: (String? _) => l10n.inventoryCreateErrorGeneric,
    unknown: (String? _) => l10n.inventoryCreateErrorGeneric,
  );
}
