String generateBatchNumber({
  DateTime? currentTime,
  int? entropy,
}) {
  final time = currentTime ?? DateTime.now();
  final dateLabel =
      '${time.year.toString().padLeft(4, '0')}'
      '${time.month.toString().padLeft(2, '0')}'
      '${time.day.toString().padLeft(2, '0')}';

  const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  final baseEntropy = (entropy ?? time.microsecondsSinceEpoch).toInt();
  final suffix = StringBuffer();
  for (var i = 0; i < 5; i++) {
    final hash = ((baseEntropy >> i) ^ (baseEntropy << (i + 3))) * 2654435761;
    suffix.write(chars[hash.abs() % chars.length]);
  }

  return 'BN-$dateLabel-$suffix';
}
