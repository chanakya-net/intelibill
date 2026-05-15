import 'package:flutter/material.dart';

class ShopStepIndicator extends StatelessWidget {
  const ShopStepIndicator({
    required this.currentStep,
    this.totalSteps = 3,
    super.key,
  }) : assert(currentStep >= 1, 'currentStep must be greater than 0'),
       assert(totalSteps >= 1, 'totalSteps must be greater than 0');

  final int currentStep;
  final int totalSteps;

  static Key _stepKey(int step) => Key('shop-step-indicator-step-$step');

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final effectiveTotal = totalSteps;

    return Row(
      children: List.generate(
        effectiveTotal * 2 - 1,
        (index) {
          final isConnector = index.isOdd;
          if (isConnector) {
            return Expanded(
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 8),
                child: Container(
                  height: 2,
                  color: theme.colorScheme.outlineVariant,
                ),
              ),
            );
          }

          final step = (index ~/ 2) + 1;
          final isActive = step == currentStep;
          return Container(
            key: _stepKey(step),
            width: 36,
            height: 36,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: isActive
                  ? theme.colorScheme.primary
                  : theme.colorScheme.surface,
              border: Border.all(
                color: isActive
                    ? theme.colorScheme.primary
                    : theme.colorScheme.outline,
                width: 2,
              ),
            ),
            child: Text(
              '$step',
              style: theme.textTheme.labelLarge?.copyWith(
                color: isActive
                    ? theme.colorScheme.onPrimary
                    : theme.colorScheme.onSurfaceVariant,
                fontWeight: FontWeight.w700,
              ),
            ),
          );
        },
      ),
    );
  }
}
