using GymShop.Application.Abstractions;
using GymShop.Application.DTOs.Orders;
using GymShop.Application.DTOs.Billing;
using GymShop.Application.UseCases.Billing;
using GymShop.Application.UseCases.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymShop.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/orders")]
public class OrdersController : ApiControllerBase
{
    private readonly IGetMyOrdersUseCase _getMyOrders;
    private readonly IGetOrderByIdUseCase _getOrderById;
    private readonly IGetOrdersUseCase _getOrders;
    private readonly IUpdateOrderStatusUseCase _updateOrderStatus;
    private readonly IGetOrderHistoryUseCase _getOrderHistory;
    private readonly ICancelOrderUseCase _cancelOrder;
    private readonly IExpirePendingOrdersUseCase _expirePendingOrders;
    private readonly IGetCustomerOrderBillingDocumentsUseCase _getCustomerBillingDocuments;
    private readonly IGetCustomerBillingDocumentPdfUseCase _getCustomerBillingDocumentPdf;
    private readonly ICurrentUserService _currentUser;

    public OrdersController(
        IGetMyOrdersUseCase getMyOrders,
        IGetOrderByIdUseCase getOrderById,
        IGetOrdersUseCase getOrders,
        IUpdateOrderStatusUseCase updateOrderStatus,
        IGetOrderHistoryUseCase getOrderHistory,
        ICancelOrderUseCase cancelOrder,
        IExpirePendingOrdersUseCase expirePendingOrders,
        IGetCustomerOrderBillingDocumentsUseCase getCustomerBillingDocuments,
        IGetCustomerBillingDocumentPdfUseCase getCustomerBillingDocumentPdf,
        ICurrentUserService currentUser)
    {
        _getMyOrders = getMyOrders;
        _getOrderById = getOrderById;
        _getOrders = getOrders;
        _updateOrderStatus = updateOrderStatus;
        _getOrderHistory = getOrderHistory;
        _cancelOrder = cancelOrder;
        _expirePendingOrders = expirePendingOrders;
        _getCustomerBillingDocuments = getCustomerBillingDocuments;
        _getCustomerBillingDocumentPdf = getCustomerBillingDocumentPdf;
        _currentUser = currentUser;
    }


    [HttpGet("my")]
    public async Task<ActionResult<List<OrderSummaryResponse>>> GetMyOrders(CancellationToken cancellationToken)
    {
        return Ok(await _getMyOrders.ExecuteAsync(_currentUser.UserId, cancellationToken));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        var canViewAll = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        return FromResult(await _getOrderById.ExecuteAsync(id, _currentUser.UserId, canViewAll, cancellationToken));
    }

    [HttpGet("{id:int}/billing-documents")]
    [ProducesResponseType(typeof(List<BillingDocumentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<BillingDocumentResponse>>> GetMyBillingDocuments(int id, CancellationToken cancellationToken) =>
        FromResult(await _getCustomerBillingDocuments.ExecuteAsync(id, _currentUser.UserId, cancellationToken));

    [HttpGet("{id:int}/billing-documents/{documentId:guid}/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> GetMyBillingDocumentPdf(int id, Guid documentId, CancellationToken cancellationToken)
    {
        var result = await _getCustomerBillingDocumentPdf.ExecuteAsync(id, documentId, _currentUser.UserId, cancellationToken);
        if (!result.IsSuccess) return ToErrorResponse(result.Error!);
        return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName, enableRangeProcessing: true);
    }

    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpGet]
    public async Task<ActionResult<PagedOrdersResponse>> GetAll([FromQuery] OrderFilterRequest filter, CancellationToken cancellationToken)
    {
        return FromResult(await _getOrders.ExecuteAsync(filter, cancellationToken));
    }

    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpGet("{id:int}/history")]
    public async Task<ActionResult<List<OrderHistoryEventResponse>>> GetHistory(int id, CancellationToken cancellationToken)
    {
        return FromResult(await _getOrderHistory.ExecuteAsync(id, cancellationToken));
    }

    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpPatch("{id:int}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> UpdateStatus(int id, UpdateOrderStatusRequest request, CancellationToken cancellationToken)
    {
        return FromResult(await _updateOrderStatus.ExecuteAsync(id, request, cancellationToken));
    }

    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpPost("{id:int}/cancel")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> Cancel(int id, CancelOrderRequest request, CancellationToken cancellationToken)
    {
        var canManageAll = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        return FromResult(await _cancelOrder.ExecuteAsync(id, _currentUser.UserId, canManageAll, request, cancellationToken));
    }

    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpPost("expire-pending")]
    public async Task<ActionResult<ExpirePendingOrdersResponse>> ExpirePending(ExpirePendingOrdersRequest request, CancellationToken cancellationToken)
    {
        return FromResult(await _expirePendingOrders.ExecuteAsync(request, cancellationToken));
    }
}


