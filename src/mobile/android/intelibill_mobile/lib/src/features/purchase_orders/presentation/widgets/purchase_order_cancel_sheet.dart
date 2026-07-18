import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';

class PurchaseOrderCancelSheet extends StatefulWidget {
  const PurchaseOrderCancelSheet({
    required this.onCancel,
    super.key,
  });

  final Future<void> Function(String reason) onCancel;

  @override
  State<PurchaseOrderCancelSheet> createState() =>
      _PurchaseOrderCancelSheetState();
}

class _PurchaseOrderCancelSheetState extends State<PurchaseOrderCancelSheet> {
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
    final trimmed = _reasonController.text.trim();
    return trimmed.length >= 1 && trimmed.length <= 500;
  }

  Future<void> _handleCancel() async {
    final trimmed = _reasonController.text.trim();
    if (!_isReasonValid) return;

    setState(() {
      _isLoading = true;
      _errorMessage = null;
    });
    try {
      await widget.onCancel(trimmed);
      if (mounted) Navigator.of(context).pop();
    } catch (e) {
      if (mounted) {
        setState(() {
          if (e is AppException) {
            _errorMessage = e.failure.message ?? 'An error occurred';
          } else {
            _errorMessage = e.toString();
          }
        });
      }
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
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
            'Cancel Purchase Order',
            style: Theme.of(context).textTheme.titleLarge,
          ),
          const SizedBox(height: 16),
          TextField(
            controller: _reasonController,
            enabled: !_isLoading,
            maxLines: 3,
            minLines: 3,
            maxLength: 500,
            decoration: const InputDecoration(
              labelText: 'Reason',
              hintText: 'Why are you cancelling this order?',
              border: OutlineInputBorder(),
              counterText: '',
            ),
            onChanged: (_) => setState(() {
              _errorMessage = null;
            }),
          ),
          if (_errorMessage != null) ...[
            const SizedBox(height: 8),
            Text(
              _errorMessage!,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
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
                child: const Text('Keep'),
              ),
              const SizedBox(width: 8),
              ElevatedButton(
                onPressed: _isLoading || !_isReasonValid ? null : _handleCancel,
                child: _isLoading
                    ? const SizedBox(
                        width: 20,
                        height: 20,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Text('Cancel Order'),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

Future<void> showPurchaseOrderCancelSheet(
  BuildContext context, {
  required Future<void> Function(String reason) onCancel,
}) {
  return showModalBottomSheet<void>(
    context: context,
    builder: (_) => PurchaseOrderCancelSheet(onCancel: onCancel),
  );
}
