import 'package:flutter_test/flutter_test.dart';
import 'package:intelibill_mobile/src/features/inventory/domain/utils/batch_number_generator.dart';

void main() {
  group('BatchNumberGenerator', () {
    test('generates deterministic batch number with injected time', () {
      final time = DateTime.utc(2026, 7, 19, 14, 30, 45, 123);
      final entropy = 12345;

      final batch1 = generateBatchNumber(currentTime: time, entropy: entropy);
      final batch2 = generateBatchNumber(currentTime: time, entropy: entropy);

      expect(batch1, batch2);
      expect(batch1, startsWith('BN-20260719-'));
    });

    test('generates different batch for different entropy', () {
      final time = DateTime.utc(2026, 7, 19);

      final batch1 = generateBatchNumber(currentTime: time, entropy: 1);
      final batch2 = generateBatchNumber(currentTime: time, entropy: 2);

      expect(batch1, isNot(batch2));
    });

    test('includes correct date in batch', () {
      final time = DateTime.utc(2020, 1, 5);

      final batch = generateBatchNumber(currentTime: time, entropy: 0);

      expect(batch, startsWith('BN-20200105-'));
    });

    test('suffix uses allowed chars only', () {
      const allowedChars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
      final time = DateTime.utc(2026, 7, 19);

      for (var e = 0; e < 100; e++) {
        final batch = generateBatchNumber(currentTime: time, entropy: e);
        final suffix = batch.split('-').last;
        for (final char in suffix.split('')) {
          expect(allowedChars.contains(char), isTrue);
        }
      }
    });

    test('suffix length is 5', () {
      final time = DateTime.utc(2026, 7, 19);

      final batch = generateBatchNumber(currentTime: time, entropy: 999);
      final suffix = batch.split('-').last;

      expect(suffix.length, 5);
    });

    test('distinct entropy values >32 produce distinct suffixes', () {
      final time = DateTime.utc(2026, 7, 19);
      final suffixes = <String>{};

      for (var e = 0; e < 100; e++) {
        final batch = generateBatchNumber(currentTime: time, entropy: e);
        suffixes.add(batch.split('-').last);
      }

      expect(suffixes.length, 100);
    });
  });
}
