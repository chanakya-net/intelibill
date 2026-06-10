import 'package:flutter/material.dart';

class AppTheme {
  static ThemeData get lightTheme {
    const backgroundGradientStart = Color(0xFFFFF8F0);
    const cardBackground = Color(0xFFFFFDF9);
    const primaryOrange = Color(0xFFF97316);
    const darkOrange = Color(0xFFEA580C);
    const onPrimaryText = Color(0xFF431407);
    const inputFill = Color(0xFFFFFBF5);

    final colorScheme = ColorScheme.fromSeed(
      seedColor: primaryOrange,
      primary: primaryOrange,
      secondary: darkOrange,
      surface: cardBackground,
      onPrimary: Colors.white,
      onSurface: const Color(0xFF6B3A16),
      surfaceTint: backgroundGradientStart,
    );

    return ThemeData(
      useMaterial3: true,
      brightness: Brightness.light,
      colorScheme: colorScheme,
      scaffoldBackgroundColor: backgroundGradientStart,
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: inputFill,
        hintStyle: TextStyle(color: Colors.brown[500]),
        labelStyle: const TextStyle(fontWeight: FontWeight.w600),
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: const BorderSide(color: Color(0xFFFCD34D)),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: const BorderSide(color: Color(0xFFFDBA74)),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: const BorderSide(color: primaryOrange, width: 2),
        ),
        errorBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: const BorderSide(color: Color(0xFFDC2626)),
        ),
        focusedErrorBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: const BorderSide(color: Color(0xFFB91C1C), width: 2),
        ),
      ),
      cardTheme: CardThemeData(
        color: cardBackground,
        elevation: 10,
        shadowColor: const Color(0x1A7C2D12),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(22)),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: primaryOrange,
          foregroundColor: Colors.white,
          textStyle: const TextStyle(
            fontWeight: FontWeight.w700,
            letterSpacing: 0.2,
          ),
          minimumSize: const Size(64, 52),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(14),
          ),
        ),
      ),
      appBarTheme: const AppBarTheme(
        backgroundColor: Colors.transparent,
        foregroundColor: onPrimaryText,
        centerTitle: true,
        elevation: 0,
      ),
      checkboxTheme: CheckboxThemeData(
        fillColor: WidgetStateProperty.resolveWith((states) {
          if (states.contains(WidgetState.selected)) {
            return primaryOrange;
          }
          if (states.contains(WidgetState.disabled)) {
            return const Color(0xFFFED7AA);
          }
          return Colors.white;
        }),
        checkColor: WidgetStateProperty.all(Colors.white),
        side: const BorderSide(color: Color(0xFFE07A2F), width: 1.5),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(5)),
      ),
      textTheme: ThemeData.light().textTheme
          .apply(fontFamily: 'Georgia')
          .copyWith(
            headlineMedium: const TextStyle(
              color: Color(0xFF7C2D12),
              fontWeight: FontWeight.w700,
            ),
            headlineSmall: const TextStyle(
              color: Color(0xFF7C2D12),
              fontWeight: FontWeight.w600,
            ),
          ),
      dialogTheme: DialogThemeData(
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
      ),
      dividerTheme: const DividerThemeData(
        color: Color(0xFFFED7AA),
        thickness: 1,
      ),
      splashFactory: InkSparkle.splashFactory,
      pageTransitionsTheme: const PageTransitionsTheme(
        builders: <TargetPlatform, PageTransitionsBuilder>{
          TargetPlatform.android: FadeUpwardsPageTransitionsBuilder(),
          TargetPlatform.iOS: CupertinoPageTransitionsBuilder(),
        },
      ),
      tooltipTheme: const TooltipThemeData(
        waitDuration: Duration(milliseconds: 250),
      ),
      elevatedButtonTheme: ElevatedButtonThemeData(
        style: ElevatedButton.styleFrom(
          backgroundColor: primaryOrange,
          foregroundColor: Colors.white,
        ),
      ),
    );
  }
}
