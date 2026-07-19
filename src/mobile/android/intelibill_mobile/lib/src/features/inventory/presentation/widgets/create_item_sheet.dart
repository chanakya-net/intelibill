import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/entities/item.dart';
import 'package:intelibill_mobile/src/features/inventory/presentation/controllers/items_controller.dart';
import 'package:intelibill_mobile/src/shared/barcode_scanner/show_barcode_scanner.dart';

class CreateItemSheet extends ConsumerStatefulWidget {
  const CreateItemSheet({this.initialName, super.key});

  final String? initialName;

  static const nameFieldKey = Key('create-item-name');
  static const barcodeFieldKey = Key('create-item-barcode');
  static const scanBarcodeButtonKey = Key('create-item-scan-barcode');
  static const uomFieldKey = Key('create-item-uom');
  static const descriptionFieldKey = Key('create-item-description');
  static const submitButtonKey = Key('create-item-submit');
  static const cancelButtonKey = Key('create-item-cancel');

  @override
  ConsumerState<CreateItemSheet> createState() => _CreateItemSheetState();
}

class _CreateItemSheetState extends ConsumerState<CreateItemSheet> {
  final _formKey = GlobalKey<FormState>();
  final _nameController = TextEditingController();
  final _barcodeController = TextEditingController();
  final _uomController = TextEditingController();
  final _descriptionController = TextEditingController();

  @override
  void initState() {
    super.initState();
    _nameController.text = widget.initialName ?? '';
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
                  l10n.inventoryCreateTitle,
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
                  key: CreateItemSheet.nameFieldKey,
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
                  key: CreateItemSheet.barcodeFieldKey,
                  controller: _barcodeController,
                  enabled: !isSubmitting,
                  textInputAction: TextInputAction.next,
                  decoration: InputDecoration(
                    labelText: l10n.inventoryCreateBarcodeLabel,
                    border: const OutlineInputBorder(),
                    suffixIcon: IconButton(
                      key: CreateItemSheet.scanBarcodeButtonKey,
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
                  key: CreateItemSheet.uomFieldKey,
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
                  key: CreateItemSheet.descriptionFieldKey,
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
                const SizedBox(height: 16),
                Row(
                  children: [
                    TextButton(
                      key: CreateItemSheet.cancelButtonKey,
                      onPressed: isSubmitting
                          ? null
                          : () => Navigator.of(context).pop(),
                      child: Text(l10n.commonCancel),
                    ),
                    const Spacer(),
                    FilledButton(
                      key: CreateItemSheet.submitButtonKey,
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
                          : Text(l10n.inventoryCreateTitle),
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

  Future<void> _submit() async {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    final description = _descriptionController.text.trim();
    final created = await ref
        .read(itemsControllerProvider.notifier)
        .createItem(
          name: _nameController.text.trim(),
          barcode: _barcodeController.text.trim(),
          uom: _uomController.text.trim(),
          description: description.isEmpty ? null : description,
        );
    if (created == null || !mounted) return;
    Navigator.of(context).pop<Item>(created);
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
