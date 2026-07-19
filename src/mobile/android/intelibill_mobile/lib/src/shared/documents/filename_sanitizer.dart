class FilenameSanitizer {
  const FilenameSanitizer._();

  static String sanitize(String value, {String fallback = 'document.pdf'}) {
    final normalized = value
        .trim()
        .replaceAll(RegExp('[^A-Za-z0-9._-]+'), '-')
        .replaceAll(RegExp('-+'), '-')
        .replaceAll(RegExp('^[-.]+'), '')
        .replaceAll(RegExp(r'[-.]+$'), '');
    if (normalized.isEmpty || normalized == 'pdf') return fallback;
    return normalized.endsWith('.pdf') ? normalized : '$normalized.pdf';
  }
}
