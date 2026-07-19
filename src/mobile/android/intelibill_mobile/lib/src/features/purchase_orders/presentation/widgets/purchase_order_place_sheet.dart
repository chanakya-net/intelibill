import 'package:flutter/material.dart';
import 'package:intelibill_mobile/src/core/errors/app_exception.dart';

class PurchaseOrderPlaceSheet extends StatefulWidget {
  const PurchaseOrderPlaceSheet({required this.onPlace, super.key});

  final Future<void> Function() onPlace;

  @override
  State<PurchaseOrderPlaceSheet> createState() =>
      _PurchaseOrderPlaceSheetState();
}

class _PurchaseOrderPlaceSheetState extends State<PurchaseOrderPlaceSheet> {
  bool _isLoading = false;
  String? _errorMessage;

  Future<void> _handlePlace() async {
    setState(() {
      _isLoading = true;
      _errorMessage = null;
    });
    try {
      await widget.onPlace();
      if (mounted) Navigator.of(context).pop();
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _errorMessage = error is AppException
            ? error.failure.message ?? 'An error occurred'
            : error.toString();
      });
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
            'Place Purchase Order',
            style: Theme.of(context).textTheme.titleLarge,
          ),
          const SizedBox(height: 16),
          Text(
            'Are you sure you want to place this purchase order?',
            style: Theme.of(context).textTheme.bodyMedium,
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
                onPressed: _isLoading ? null : () => Navigator.of(context).pop(),
                child: const Text('Cancel'),
              ),
              const SizedBox(width: 8),
              ElevatedButton(
                onPressed: _isLoading ? null : _handlePlace,
                child: _isLoading
                    ? const SizedBox(
                        width: 20,
                        height: 20,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Text('Place Order'),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

Future<void> showPurchaseOrderPlaceSheet(
  BuildContext context, {
  required Future<void> Function() onPlace,
}) {
  return showModalBottomSheet<void>(
    context: context,
    builder: (_) => PurchaseOrderPlaceSheet(onPlace: onPlace),
  );
}
