import 'package:equatable/equatable.dart';

class BankAccount extends Equatable {
  const BankAccount({
    required this.id,
    required this.bankName,
    required this.accountNumber,
    this.accountType,
    this.ifscCode,
    this.accountHolderName,
  });

  final String id;
  final String bankName;
  final String accountNumber;
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
