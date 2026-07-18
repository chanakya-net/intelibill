import 'package:flutter/material.dart';

class PurchaseOrderProgress extends StatelessWidget {
  const PurchaseOrderProgress({
    required this.expectedQuantity,
    required this.receivedQuantity,
    super.key,
  });

  final int expectedQuantity;
  final int receivedQuantity;

  @override
  Widget build(BuildContext context) {
    final progress = expectedQuantity == 0
        ? 0.0
        : (receivedQuantity / expectedQuantity).clamp(0.0, 1.0);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('Received: $receivedQuantity / $expectedQuantity'),
        const SizedBox(height: 4),
        LinearProgressIndicator(value: progress),
      ],
    );
  }
}
