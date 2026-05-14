import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/app/app.dart';

void main() {
  runApp(
    const ProviderScope(
      child: IntelibillApp(),
    ),
  );
}
