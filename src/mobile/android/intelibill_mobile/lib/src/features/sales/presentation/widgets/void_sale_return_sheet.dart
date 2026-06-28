import 'dart:async';

import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/errors/failure.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';
import 'package:intelibill_mobile/src/features/sales/domain/entities/sale_detail.dart';

Future<bool?> showVoidSaleReturnSheet(
  BuildContext context, {
  required SaleDetailReturn saleReturn,
  required AppLocalizations l10n,
  required Future<Failure?> Function(String reason) onVoid,
}) {
  return showModalBottomSheet<bool>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (sheetContext) {
      return VoidSaleReturnSheet(
        saleReturn: saleReturn,
        l10n: l10n,
        onVoid: onVoid,
      );
    },
  );
}

class VoidSaleReturnSheet extends StatefulWidget {
  const VoidSaleReturnSheet({
    required this.saleReturn,
    required this.l10n,
    required this.onVoid,
    super.key,
  });

  final SaleDetailReturn saleReturn;
  final AppLocalizations l10n;
  final Future<Failure?> Function(String reason) onVoid;

  @override
  State<VoidSaleReturnSheet> createState() => _VoidSaleReturnSheetState();
}

class _VoidSaleReturnSheetState extends State<VoidSaleReturnSheet> {
  final _reasonController = TextEditingController();
  bool _isSubmitting = false;
  Failure? _failure;

  @override
  void dispose() {
    _reasonController.dispose();
    super.dispose();
  }

  Future<void> _submitVoid() async {
    final reason = _reasonController.text.trim();
    if (_isSubmitting) {
      return;
    }
    if (reason.isEmpty) {
      setState(
        () => _failure = Failure.validation(
          message: widget.l10n.salesDetailVoidReturnReasonRequired,
        ),
      );
      return;
    }

    setState(() {
      _isSubmitting = true;
      _failure = null;
    });
    final failure = await widget.onVoid(reason);
    if (!mounted) {
      return;
    }

    setState(() => _isSubmitting = false);
    if (failure == null) {
      Navigator.of(context).pop(true);
      return;
    }

    setState(() => _failure = failure);
  }

  @override
  Widget build(BuildContext context) {
    return SafeArea(
      child: Padding(
        padding: EdgeInsets.fromLTRB(
          16,
          0,
          16,
          24 + MediaQuery.of(context).viewInsets.bottom,
        ),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(
                '${widget.l10n.salesDetailVoidReturnAction} '
                '${widget.saleReturn.returnNumber}',
                style: Theme.of(context).textTheme.titleLarge,
              ),
              const SizedBox(height: 8),
              TextField(
                key: Key(
                  voidReturnReasonFieldKey(widget.saleReturn.saleReturnId),
                ),
                controller: _reasonController,
                decoration: InputDecoration(
                  labelText: widget.l10n.salesDetailVoidReturnReason,
                  border: const OutlineInputBorder(),
                ),
              ),
              if (_failure != null) ...[
                const SizedBox(height: 12),
                Text(
                  _failureMessage(widget.l10n, _failure!),
                  key: Key(
                    voidReturnFailureKey(widget.saleReturn.saleReturnId),
                  ),
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    color: Theme.of(context).colorScheme.error,
                  ),
                ),
              ],
              const SizedBox(height: 12),
              Row(
                mainAxisAlignment: MainAxisAlignment.end,
                children: [
                  TextButton(
                    key: Key(
                      voidReturnSubmitKey(widget.saleReturn.saleReturnId),
                    ),
                    onPressed: _isSubmitting
                        ? null
                        : () => unawaited(_submitVoid()),
                    child: _isSubmitting
                        ? const SizedBox(
                            height: 16,
                            width: 16,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : Text(widget.l10n.salesDetailVoidReturnAction),
                  ),
                  TextButton(
                    onPressed: () => Navigator.of(context).pop(),
                    child: Text(widget.l10n.commonCancel),
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

String voidReturnActionKey(String action, String id) {
  return 'sales-detail-return-$action-$id';
}

String voidReturnReasonFieldKey(String id) {
  return voidReturnActionKey('void-reason', id);
}

String voidReturnSubmitKey(String id) {
  return voidReturnActionKey('void-submit', id);
}

String voidReturnFailureKey(String id) {
  return voidReturnActionKey('void-failure', id);
}

String _failureMessage(AppLocalizations l10n, Failure failure) {
  return failure.when(
    validation: (message, _) => message ?? l10n.salesDetailVoidReturnFailed,
    unauthorized: (message) =>
        message ?? l10n.salesDetailVoidReturnUnauthorized,
    forbidden: (message) => message ?? l10n.salesDetailVoidReturnForbidden,
    notFound: (message) => message ?? l10n.salesDetailVoidReturnFailed,
    server: (message, _) => message ?? l10n.salesDetailVoidReturnFailed,
    network: (message) => message ?? l10n.salesDetailVoidReturnNetwork,
    timeout: (message) => message ?? l10n.salesDetailVoidReturnTimeout,
    serialization: (message) => message ?? l10n.salesDetailVoidReturnFailed,
    unknown: (message) => message ?? l10n.salesDetailVoidReturnFailed,
  );
}
