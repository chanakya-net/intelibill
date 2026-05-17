import 'package:equatable/equatable.dart';

class CreateShopRequest extends Equatable {
  const CreateShopRequest({
    required this.name,
    required this.address,
    required this.city,
    required this.state,
    required this.pincode,
    this.contactPerson,
    this.mobileNumber,
    this.gstNumber,
  });

  final String name;
  final String address;
  final String city;
  final String state;
  final String pincode;
  final String? contactPerson;
  final String? mobileNumber;
  final String? gstNumber;

  @override
  List<Object?> get props => [
    name,
    address,
    city,
    state,
    pincode,
    contactPerson,
    mobileNumber,
    gstNumber,
  ];
}
