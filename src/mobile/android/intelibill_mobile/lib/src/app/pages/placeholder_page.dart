import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import 'package:intelibill_mobile/src/app/router/app_router.dart';
import 'package:intelibill_mobile/src/core/localization/app_localizations.dart';

@immutable
class PlaceholderPageDetails {
  const PlaceholderPageDetails({
    required this.title,
    required this.body,
  });

  final String title;
  final String body;
}

class PlaceholderPage extends StatelessWidget {
  const PlaceholderPage({
    required this.title,
    required this.body,
    super.key,
  });

  final String title;
  final String body;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(
        title: Text(title),
        actions: [
          IconButton(
            tooltip: AppLocalizations.of(context)!.shellProfile,
            icon: const Icon(Icons.account_circle),
            onPressed: () => context.push(AppRoutes.profile),
          ),
        ],
      ),
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Text(
            body,
            textAlign: TextAlign.center,
            style: theme.textTheme.bodyLarge,
          ),
        ),
      ),
    );
  }
}
