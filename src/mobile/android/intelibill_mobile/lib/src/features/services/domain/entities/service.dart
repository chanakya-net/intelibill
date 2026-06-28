import 'package:equatable/equatable.dart';

class Service extends Equatable {
  const Service({
    required this.serviceId,
    required this.code,
    required this.name,
    this.description,
    required this.price,
    this.hsnCode,
    required this.taxRatePercent,
    required this.taxIncluded,
    required this.isActive,
  });

  final String serviceId;
  final String code;
  final String name;
  final String? description;
  final double price;
  final String? hsnCode;
  final double taxRatePercent;
  final bool taxIncluded;
  final bool isActive;

  @override
  List<Object?> get props => [
    serviceId,
    code,
    name,
    description,
    price,
    hsnCode,
    taxRatePercent,
    taxIncluded,
    isActive,
  ];
}
