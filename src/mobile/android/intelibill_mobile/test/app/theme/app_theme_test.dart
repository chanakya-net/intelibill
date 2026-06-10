import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/app/theme/app_theme.dart';

void main() {
  group('AppTheme', () {
    test('lightTheme uses Material 3', () {
      final theme = AppTheme.lightTheme;
      expect(theme.useMaterial3, isTrue);
    });

    test('lightTheme has light brightness', () {
      final theme = AppTheme.lightTheme;
      expect(theme.brightness, equals(Brightness.light));
    });

    test('lightTheme has valid colorScheme', () {
      final theme = AppTheme.lightTheme;
      expect(theme.colorScheme, isNotNull);
      expect(theme.colorScheme.brightness, equals(Brightness.light));
    });

    test('lightTheme configures navigation and bottom sheet styling', () {
      final theme = AppTheme.lightTheme;

      expect(theme.navigationBarTheme.backgroundColor, isNotNull);
      expect(theme.navigationBarTheme.indicatorColor, isNotNull);
      expect(theme.bottomSheetTheme.showDragHandle, isTrue);
      expect(theme.bottomSheetTheme.shape, isNotNull);
    });
  });
}
