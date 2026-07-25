import 'package:flutter/material.dart';

/// Shared layout primitives for purchase order detail surfaces.
class PurchaseOrderDetailSectionCard extends StatelessWidget {
  const PurchaseOrderDetailSectionCard({
    required this.title,
    required this.child,
    super.key,
  });

  final String title;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              title,
              style: theme.textTheme.titleMedium?.copyWith(
                fontWeight: FontWeight.w700,
              ),
            ),
            const SizedBox(height: 12),
            child,
          ],
        ),
      ),
    );
  }
}

/// Preserves `Label: value` copy for widget tests while applying detail styling.
class PurchaseOrderDetailInfoLine extends StatelessWidget {
  const PurchaseOrderDetailInfoLine({
    required this.label,
    required this.value,
    this.style,
    super.key,
  });

  final String label;
  final String value;
  final TextStyle? style;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final text = label.endsWith(':') ? '$label $value' : '$label: $value';
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Text(
        text,
        style:
            style ??
            theme.textTheme.bodyMedium?.copyWith(
              color: theme.colorScheme.onSurface,
            ),
      ),
    );
  }
}
