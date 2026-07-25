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

  static const _connectorBorder = Color(0xFFFDBA74);

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final connectorColor = theme.dividerTheme.color ?? const Color(0xFFFED7AA);

    return Card(
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
        child: Row(
          children: List.generate(totalSteps * 2 - 1, (index) {
            final isConnector = index.isOdd;
            if (isConnector) {
              final leftStep = index ~/ 2;
              final isCompleted = leftStep < currentStep;
              return Expanded(
                child: Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 6),
                  child: Container(
                    height: 2,
                    decoration: BoxDecoration(
                      color: isCompleted
                          ? theme.colorScheme.primary
                          : connectorColor,
                      borderRadius: BorderRadius.circular(1),
                    ),
                  ),
                ),
              );
            }

            final step = (index ~/ 2) + 1;
            final isActive = step == currentStep;
            final isCompleted = step < currentStep;

            return Container(
              key: _stepKey(step),
              width: 36,
              height: 36,
              alignment: Alignment.center,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: isActive || isCompleted
                    ? theme.colorScheme.primary
                    : theme.cardTheme.color ?? theme.colorScheme.surface,
                border: Border.all(
                  color: isActive || isCompleted
                      ? theme.colorScheme.primary
                      : _connectorBorder,
                  width: 2,
                ),
                boxShadow: isActive
                    ? [
                        BoxShadow(
                          color: theme.colorScheme.primary.withValues(
                            alpha: 0.25,
                          ),
                          blurRadius: 8,
                          offset: const Offset(0, 2),
                        ),
                      ]
                    : null,
              ),
              child: Text(
                '$step',
                style: theme.textTheme.labelLarge?.copyWith(
                  color: isActive || isCompleted
                      ? theme.colorScheme.onPrimary
                      : theme.colorScheme.onSurface.withValues(alpha: 0.6),
                  fontWeight: FontWeight.w700,
                ),
              ),
            );
          }),
        ),
      ),
    );
  }
}
