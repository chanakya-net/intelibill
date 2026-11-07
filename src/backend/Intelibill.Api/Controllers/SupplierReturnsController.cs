using Intelibill.Api.Extensions;
using Intelibill.Application.Features.SupplierReturns.DTOs;
using Intelibill.Application.Features.SupplierReturns.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;
[ApiController, Authorize, Route("api/supplier-returns")]
public sealed class SupplierReturnsController(IMessageBus bus, SupplierReturnService service) : AuthenticatedControllerBase(bus)
{
    [HttpPost("preview")]
    public async Task<IActionResult> Preview(SupplierReturnPreviewRequest request,CancellationToken ct)
    {
        var auth=CheckAuthAndShop(); if(auth is not null) return auth;
        return (await service.PreviewAsync(UserId!.Value,ActiveShopId!.Value,request,ct)).ToActionResult(Ok);
    }
    [HttpPost]
    public async Task<IActionResult> Record(SupplierReturnRequest request,CancellationToken ct)
    {
        var auth=CheckAuthAndShop(); if(auth is not null) return auth;
        return (await service.RecordAsync(UserId!.Value,ActiveShopId!.Value,request,ct)).ToActionResult(Ok);
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id,CancellationToken ct)
    {
        var auth=CheckAuthAndShop(); if(auth is not null) return auth;
        return (await service.GetAsync(UserId!.Value,ActiveShopId!.Value,id,ct)).ToActionResult(Ok);
    }
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct,Guid? supplierId=null,Guid? receiptValueId=null,string? status=null,int page=1,int pageSize=20)
    {
        var auth=CheckAuthAndShop(); if(auth is not null) return auth;
        return (await service.ListAsync(UserId!.Value,ActiveShopId!.Value,supplierId,receiptValueId,status,page,pageSize,ct)).ToActionResult(Ok);
    }
    [HttpPost("{id:guid}/credit")]
    public async Task<IActionResult> Credit(Guid id,SupplierReturnCreditRequest request,CancellationToken ct)
    {
        var auth=CheckAuthAndShop(); if(auth is not null) return auth;
        return (await service.AcceptAsync(UserId!.Value,ActiveShopId!.Value,id,request,ct)).ToActionResult(Ok);
    }
}
[ApiController, Authorize, Route("api/purchase-receipts")]
public sealed class PurchaseReceiptsController(IMessageBus bus,SupplierReturnService service) : AuthenticatedControllerBase(bus)
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id,CancellationToken ct)
    {
        var auth=CheckAuthAndShop(); if(auth is not null) return auth;
        return (await service.GetReceiptAsync(UserId!.Value,ActiveShopId!.Value,id,ct)).ToActionResult(Ok);
    }
    [HttpPost("{id:guid}/reversal")]
    public async Task<IActionResult> Reverse(Guid id,ReceiptReversalRequest request,CancellationToken ct)
    {
        var auth=CheckAuthAndShop(); if(auth is not null) return auth;
        return (await service.ReverseAsync(UserId!.Value,ActiveShopId!.Value,id,false,request.OperationKey,request.Reason,[new(id,request.ExpectedReceiptVersion,request.ExpectedBatchVersion)],ct)).ToActionResult(Ok);
    }
    [HttpPost("/api/purchase-order-receipts/{id:guid}/reversal")]
    public async Task<IActionResult> ReverseGroup(Guid id,GroupReceiptReversalRequest request,CancellationToken ct)
    {
        var auth=CheckAuthAndShop(); if(auth is not null) return auth;
        return (await service.ReverseAsync(UserId!.Value,ActiveShopId!.Value,id,true,request.OperationKey,request.Reason,request.Lines,ct)).ToActionResult(Ok);
    }
}
