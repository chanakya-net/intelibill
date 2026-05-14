import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intelibill_mobile/src/features/app_status/domain/entities/app_status.dart';
import 'package:intelibill_mobile/src/features/app_status/presentation/controllers/app_status_controller.dart';
import 'package:intl/intl.dart';

class AppStatusPage extends ConsumerWidget {
  const AppStatusPage({super.key});

  static const _dateFormatPattern = "yyyy-MM-dd HH:mm:ss 'UTC'";

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final statusState = ref.watch(appStatusControllerProvider);

    return Scaffold(
      appBar: AppBar(
        title: const Text('Intelibill status'),
      ),
      body: statusState.when(
        data: (status) => _StatusContent(status: status),
        loading: () => const Center(
          child: CircularProgressIndicator(),
        ),
        error: (error, stackTrace) => Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Text(
                  'Unable to load app status',
                  style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
                ),
                const SizedBox(height: 8),
                Text(
                  error.toString(),
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: 16),
                FilledButton(
                  onPressed: () {
                    unawaited(
                      ref.read(appStatusControllerProvider.notifier).refresh(),
                    );
                  },
                  child: const Text('Retry'),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _StatusContent extends StatelessWidget {
  const _StatusContent({required this.status});

  final AppStatus status;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final formattedTimestamp = DateFormat(
      AppStatusPage._dateFormatPattern,
    ).format(status.timestamp.toUtc());

    return ListView(
      padding: const EdgeInsets.all(24),
      children: [
        Text(
          'Mobile clean architecture sample',
          style: theme.textTheme.headlineSmall,
        ),
        const SizedBox(height: 8),
        Text(
          'This screen proves DTO mapping, repository wiring, Riverpod '
          'controllers, and test hooks without adding backend endpoints.',
          style: theme.textTheme.bodyLarge,
        ),
        const SizedBox(height: 24),
        _InfoCard(
          label: 'Status',
          value: status.statusText,
        ),
        const SizedBox(height: 12),
        _InfoCard(
          label: 'API base URL',
          value: status.apiBaseUrl,
        ),
        const SizedBox(height: 12),
        _InfoCard(
          label: 'Environment',
          value: status.environment ?? 'unknown',
        ),
        const SizedBox(height: 12),
        _InfoCard(
          label: 'Updated',
          value: formattedTimestamp,
        ),
      ],
    );
  }
}

class _InfoCard extends StatelessWidget {
  const _InfoCard({
    required this.label,
    required this.value,
  });

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              label,
              style: theme.textTheme.labelLarge,
            ),
            const SizedBox(height: 8),
            SelectableText(
              value,
              style: theme.textTheme.titleMedium,
            ),
          ],
        ),
      ),
    );
  }
}
