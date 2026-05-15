import 'package:intelibill_mobile/src/features/shops/data/dtos/add_bank_account_request_dto.dart';
import 'package:intelibill_mobile/src/features/shops/data/dtos/create_shop_request_dto.dart';
import 'package:intelibill_mobile/src/features/shops/data/dtos/shop_details_dto.dart';
import 'package:intelibill_mobile/src/features/shops/data/dtos/update_shop_request_dto.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/add_bank_account_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/create_shop_request.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/shop_details.dart';
import 'package:intelibill_mobile/src/features/shops/domain/entities/update_shop_request.dart';

class ShopMapper {
  static ShopDetails toDomain(ShopDetailsDto dto) {
    final hasPrimaryAccount =
        (dto.bankName?.isNotEmpty ?? false) &&
        (dto.bankAccountNumber?.isNotEmpty ?? false);

    return ShopDetails(
      id: dto.shopId,
      name: dto.name,
      address: dto.address,
      city: dto.city,
      state: dto.state,
      pincode: dto.pincode,
      contactPerson: dto.contactPerson,
      mobileNumber: dto.mobileNumber,
      gstNumber: dto.gstNumber,
      bankAccounts: hasPrimaryAccount
          ? [
              BankAccount(
                bankName: dto.bankName,
                accountNumber: dto.bankAccountNumber,
                accountType: dto.bankAccountType,
                ifscCode: dto.ifscCode,
                accountHolderName: dto.accountHolderName,
              ),
            ]
          : const [],
    );
  }

  static CreateShopRequestDto toCreateDto(CreateShopRequest request) {
    return CreateShopRequestDto(
      name: request.name,
      address: request.address,
      city: request.city,
      state: request.state,
      pincode: request.pincode,
      contactPerson: request.contactPerson,
      mobileNumber: request.mobileNumber,
      gstNumber: request.gstNumber,
    );
  }

  static UpdateShopRequestDto toUpdateDto(UpdateShopRequest request) {
    return UpdateShopRequestDto(
      name: request.name,
      address: request.address,
      city: request.city,
      state: request.state,
      pincode: request.pincode,
      contactPerson: request.contactPerson,
      mobileNumber: request.mobileNumber,
      gstNumber: request.gstNumber,
    );
  }

  static AddBankAccountRequestDto toAddBankAccountDto(
    AddBankAccountRequest request,
  ) {
    return AddBankAccountRequestDto(
      bankName: request.bankName,
      accountNumber: request.accountNumber,
      accountType: request.accountType,
      ifscCode: request.ifscCode,
      accountHolderName: request.accountHolderName,
    );
  }
}
