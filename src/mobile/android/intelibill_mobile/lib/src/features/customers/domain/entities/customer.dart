import 'package:equatable/equatable.dart';

class Customer extends Equatable {
  const Customer({
    required this.customerId,
    required this.name,
    required this.phoneNumber,
    this.address,
    required this.isActive,
    this.outstandingDue = 0.0,
  });

  final String customerId;
  final String name;
  final String phoneNumber;
  final String? address;
  final bool isActive;
  final double outstandingDue;

  @override
  List<Object?> get props => [
    customerId,
    name,
    phoneNumber,
    address,
    isActive,
    outstandingDue,
  ];
}
