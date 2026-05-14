import 'package:equatable/equatable.dart';

class AppStatus extends Equatable {
  const AppStatus({
    required this.statusText,
    required this.apiBaseUrl,
    required this.timestamp,
    this.environment,
  });

  final String statusText;
  final String apiBaseUrl;
  final DateTime timestamp;
  final String? environment;

  @override
  List<Object?> get props => [statusText, apiBaseUrl, timestamp, environment];
}
