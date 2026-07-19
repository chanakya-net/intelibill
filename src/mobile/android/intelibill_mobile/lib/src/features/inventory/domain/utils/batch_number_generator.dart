String generateBatchNumber({
  DateTime? currentTime,
  int? entropy,
}) {
  final time = currentTime ?? DateTime.now();
  final dateLabel = '${time.year.toString().padLeft(4, '0')}'
      '${time.month.toString().padLeft(2, '0')}'
      '${time.day.toString().padLeft(2, '0')}';

  const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  final seed = (entropy ?? time.microsecondsSinceEpoch).toInt();
  final suffix = StringBuffer();
  for (var i = 0; i < 5; i++) {
    suffix.write(chars[(seed + (i * 17)).abs() % chars.length]);
  }

  return 'BN-$dateLabel-$suffix';
}
