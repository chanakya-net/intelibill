import 'package:equatable/equatable.dart';

class Supplier extends Equatable {
  const Supplier({
    required this.supplierId,
    required this.name,
    this.contactPersonName,
    this.contactPersonPhone,
    this.address,
    this.city,
    this.state,
    this.pin,
    required this.isSystem,
    required this.isActive,
    required this.isPreferred,
    required this.balanceDue,
  });

  final String supplierId;
  final String name;
  final String? contactPersonName;
  final String? contactPersonPhone;
  final String? address;
  final String? city;
  final String? state;
  final String? pin;
  final bool isSystem;
  final bool isActive;
  final bool isPreferred;
  final double balanceDue;

  @override
  List<Object?> get props => [
    supplierId,
    name,
    contactPersonName,
    contactPersonPhone,
    address,
    city,
    state,
    pin,
    isSystem,
    isActive,
    isPreferred,
    balanceDue,
  ];
}
