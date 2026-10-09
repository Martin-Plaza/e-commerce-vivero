using GymShop.Application.DTOs.Carts;
using GymShop.Application.DTOs.Orders;
using GymShop.Application.UseCases.Carts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymShop.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/guest-checkout")]
public sealed class GuestCheckoutController(IGuestCheckoutUseCase guestCheckout) : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<GuestCheckoutResponse>> Create(GuestCheckoutRequest request, CancellationToken cancellationToken) =>
        FromResult(await guestCheckout.ExecuteAsync(request, cancellationToken));

    [HttpGet("orders/{id:int}")]
    public async Task<ActionResult<OrderResponse>> GetOrder(int id, [FromQuery] Guid accessToken, CancellationToken cancellationToken) =>
        FromResult(await guestCheckout.GetOrderAsync(id, accessToken, cancellationToken));

    [HttpGet("sessions/{id:int}")]
    public async Task<ActionResult<CheckoutResponse>> GetCheckout(int id, [FromQuery] Guid accessToken, CancellationToken cancellationToken) =>
        FromResult(await guestCheckout.GetCheckoutAsync(id, accessToken, cancellationToken));
}
