import 'package:equatable/equatable.dart';

class BankAccount extends Equatable {
  const BankAccount({
    this.id,
    this.bankName,
    this.accountNumber,
    this.accountType,
    this.ifscCode,
    this.accountHolderName,
  });

  final String? id;
  final String? bankName;
  final String? accountNumber;
  final String? accountType;
  final String? ifscCode;
  final String? accountHolderName;

  @override
  List<Object?> get props => [
        id,
        bankName,
        accountNumber,
        accountType,
        ifscCode,
        accountHolderName,
      ];
}

class ShopDetails extends Equatable {
  const ShopDetails({
    required this.id,
    required this.name,
    required this.address,
    required this.city,
    required this.state,
    required this.pincode,
    this.contactPerson,
    this.mobileNumber,
    this.gstNumber,
    required this.bankAccounts,
  });

  final String id;
  final String name;
  final String address;
  final String city;
  final String state;
  final String pincode;
  final String? contactPerson;
  final String? mobileNumber;
  final String? gstNumber;
  final List<BankAccount> bankAccounts;

  @override
  List<Object?> get props => [
        id,
        name,
        address,
        city,
        state,
        pincode,
        contactPerson,
        mobileNumber,
        gstNumber,
        bankAccounts,
      ];
}
