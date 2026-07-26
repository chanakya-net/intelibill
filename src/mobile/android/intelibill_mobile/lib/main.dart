import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/app/app.dart';
import 'package:intelibill_mobile/src/core/config/app_config.dart';
import 'package:intl/date_symbol_data_local.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  AppConfig.assertConfiguredForRelease();
  await initializeDateFormatting();
  runApp(const ProviderScope(child: IntelibillApp()));
}
