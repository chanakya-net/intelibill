import 'package:equatable/equatable.dart';

class BarcodeScanResult extends Equatable {
  const BarcodeScanResult({required this.value, this.format});

  final String value;
  final String? format;

  @override
  List<Object?> get props => [value, format];
}
