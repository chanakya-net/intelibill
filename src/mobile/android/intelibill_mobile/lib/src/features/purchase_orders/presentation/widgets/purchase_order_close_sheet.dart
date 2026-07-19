import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/purchase_orders/presentation/localization/purchase_order_messages.dart';

class PurchaseOrderCloseSheet extends StatefulWidget {
  const PurchaseOrderCloseSheet({required this.onClose, super.key});

  final Future<void> Function(String reason) onClose;

  @override
  State<PurchaseOrderCloseSheet> createState() =>
      _PurchaseOrderCloseSheetState();
}

class _PurchaseOrderCloseSheetState extends State<PurchaseOrderCloseSheet> {
  late final TextEditingController _reasonController;
  bool _isLoading = false;
  String? _errorMessage;

  @override
  void initState() {
    super.initState();
    _reasonController = TextEditingController();
  }

  @override
  void dispose() {
    _reasonController.dispose();
    super.dispose();
  }

  bool get _isReasonValid {
    final reason = _reasonController.text.trim();
    return reason.isNotEmpty && reason.length <= 500;
  }

  Future<void> _handleClose() async {
    final reason = _reasonController.text.trim();
    if (!_isReasonValid) return;
    setState(() {
      _isLoading = true;
      _errorMessage = null;
    });
    try {
      await widget.onClose(reason);
      if (mounted) Navigator.of(context).pop();
    } on AppException catch (error) {
      if (!mounted) return;
      setState(() {
        _errorMessage = purchaseOrderFailureMessage(
          AppLocalizations.of(context)!,
          error.failure,
        );
      });
    } on Object {
      if (!mounted) return;
      setState(() {
        _errorMessage = purchaseOrderFailureMessage(
          AppLocalizations.of(context)!,
          const Failure.unknown(),
        );
      });
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;
    return Padding(
      padding: EdgeInsets.only(
        left: 16,
        right: 16,
        top: 16,
        bottom: MediaQuery.of(context).viewInsets.bottom + 16,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            l10n.purchaseOrderCloseTitle,
            style: Theme.of(context).textTheme.titleLarge,
          ),
          const SizedBox(height: 16),
          TextField(
            controller: _reasonController,
            enabled: !_isLoading,
            maxLines: 3,
            minLines: 3,
            decoration: InputDecoration(
              labelText: l10n.purchaseOrderCloseReasonLabel,
              hintText: l10n.purchaseOrderCloseReasonHint,
              border: const OutlineInputBorder(),
              counterText: '',
            ),
            onChanged: (_) => setState(() => _errorMessage = null),
          ),
          if (_errorMessage != null) ...[
            const SizedBox(height: 8),
            Semantics(
              liveRegion: true,
              child: Text(
                _errorMessage!,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            ),
          ],
          const SizedBox(height: 16),
          Row(
            mainAxisAlignment: MainAxisAlignment.end,
            children: [
              TextButton(
                onPressed: _isLoading
                    ? null
                    : () => Navigator.of(context).pop(),
                child: Text(l10n.purchaseOrderCloseKeepAction),
              ),
              const SizedBox(width: 8),
              ElevatedButton(
                onPressed: _isLoading || !_isReasonValid ? null : _handleClose,
                child: _isLoading
                    ? const SizedBox(
                        width: 20,
                        height: 20,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : Text(l10n.purchaseOrderCloseConfirmAction),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

Future<void> showPurchaseOrderCloseSheet(
  BuildContext context, {
  required Future<void> Function(String reason) onClose,
}) {
  return showModalBottomSheet<void>(
    context: context,
    builder: (_) => PurchaseOrderCloseSheet(onClose: onClose),
  );
}
