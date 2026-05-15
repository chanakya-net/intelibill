import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/core/utils/date_time_wire.dart';

void main() {
  group('date time wire helpers', () {
    test('formats instants as UTC ISO strings', () {
      final localInstant = DateTime(2026, 5, 15, 9, 30);

      expect(
        formatUtcIsoInstant(localInstant),
        localInstant.toUtc().toIso8601String(),
      );
    });

    test('formats date-only values from local calendar fields', () {
      final localDate = DateTime(2026, 5, 15, 0, 30);

      expect(formatLocalIsoDate(localDate), '2026-05-15');
    });
  });
}
